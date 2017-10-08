using SpotifyAPI.Local;
using SpotifyAPI.Local.Enums;
using SpotifyAPI.Local.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace SpotifyAPI.Example
{
    public partial class LocalControl : UserControl
    {
        private readonly SpotifyLocalAPI _spotify;
        private Track _currentTrack;
        private bool _isRecording;
        private Process _recordingProcess;

        public LocalControl()
        {
            InitializeComponent();

            _spotify = new SpotifyLocalAPI();
            _spotify.OnPlayStateChange += _spotify_OnPlayStateChange;
            _spotify.OnTrackChange += _spotify_OnTrackChange;
            _spotify.OnTrackTimeChange += _spotify_OnTrackTimeChange;
            _spotify.OnVolumeChange += _spotify_OnVolumeChange;
            //_spotify.SynchronizingObject = this;

            artistLinkLabel.Click += (sender, args) => Process.Start(artistLinkLabel.Tag.ToString());
            albumLinkLabel.Click += (sender, args) => Process.Start(albumLinkLabel.Tag.ToString());
            titleLinkLabel.Click += (sender, args) => Process.Start(titleLinkLabel.Tag.ToString());
        }

        public void Connect()
        {
            if (!SpotifyLocalAPI.IsSpotifyRunning())
            {
                MessageBox.Show(@"Spotify isn't running!");
                return;
            }
            if (!SpotifyLocalAPI.IsSpotifyWebHelperRunning())
            {
                MessageBox.Show(@"SpotifyWebHelper isn't running!");
                return;
            }

            var successful = _spotify.Connect();
            if (successful)
            {
                connectBtn.Text = @"Connection to Spotify successful";
                connectBtn.Enabled = false;
                UpdateInfos();
                _spotify.ListenForEvents = true;
            }
            else
            {
                var res = MessageBox.Show(@"Couldn't connect to the spotify client. Retry?", @"Spotify", MessageBoxButtons.YesNo);
                if (res == DialogResult.Yes)
                    Connect();
            }
        }

        public void UpdateInfos()
        {
            var status = _spotify.GetStatus();
            if (status == null)
                return;

            //Basic Spotify Infos
            UpdatePlayingStatus(status.Playing);
            clientVersionLabel.Text = status.ClientVersion;
            versionLabel.Text = status.Version.ToString();
            repeatShuffleLabel.Text = status.Repeat + @" and " + status.Shuffle;

            if (status.Track != null) //Update track infos
                UpdateTrack(status.Track);
        }

        public async void UpdateTrack(Track track)
        {
            _currentTrack = track;

            advertLabel.Text = track.IsAd() ? "ADVERT" : "";
            timeProgressBar.Maximum = track.Length;
            timeProgressBar.Value = 0;

            if (track.IsAd())
                return; //Don't process further, maybe null values

            titleLinkLabel.Text = track.TrackResource.Name;
            titleLinkLabel.Tag = track.TrackResource.Uri;

            artistLinkLabel.Text = track.ArtistResource.Name;
            artistLinkLabel.Tag = track.ArtistResource.Uri;

            albumLinkLabel.Text = track.AlbumResource.Name;
            albumLinkLabel.Tag = track.AlbumResource.Uri;

            var uri = track.TrackResource.ParseUri();

            trackInfoBox.Text = $@"Track Info - {uri.Id}";

            bigAlbumPicture.Image = await track.GetAlbumArtAsync(AlbumArtSize.Size640);
            smallAlbumPicture.Image = await track.GetAlbumArtAsync(AlbumArtSize.Size160);
        }

        public void UpdatePlayingStatus(bool playing)
        {
            isPlayingLabel.Text = playing.ToString();
        }

        private void _spotify_OnVolumeChange(object sender, VolumeChangeEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => _spotify_OnVolumeChange(sender, e)));
                return;
            }
            volumeLabel.Text = (e.NewVolume * 100).ToString(CultureInfo.InvariantCulture);
        }

        private void _spotify_OnTrackTimeChange(object sender, TrackTimeChangeEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => _spotify_OnTrackTimeChange(sender, e)));
                return;
            }
            timeLabel.Text = $@"{FormatTime(e.TrackTime)}/{FormatTime(_currentTrack.Length)}";
            if(e.TrackTime < _currentTrack.Length)
                timeProgressBar.Value = (int)e.TrackTime;
        }

        private void _spotify_OnTrackChange(object sender, TrackChangeEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => _spotify_OnTrackChange(sender, e)));
                return;
            }
            StopRecording();

            UpdateTrack(e.NewTrack);
            if (_isRecording)
            {
                RecordCurrentTrack();
            }
        }

        private void _spotify_OnPlayStateChange(object sender, PlayStateEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => _spotify_OnPlayStateChange(sender, e)));
                return;
            }
            UpdatePlayingStatus(e.Playing);
        }

        private void connectBtn_Click(object sender, EventArgs e)
        {
            Connect();
        }

        private async void playUrlBtn_Click(object sender, EventArgs e)
        {
            await _spotify.PlayURL(playTextBox.Text, contextTextBox.Text);
        }

        private async void playBtn_Click(object sender, EventArgs e)
        {
            await _spotify.Play();
        }

        private async void pauseBtn_Click(object sender, EventArgs e)
        {
            await _spotify.Pause();
        }

        private void prevBtn_Click(object sender, EventArgs e)
        {
            _spotify.Previous();
        }

        private void skipBtn_Click(object sender, EventArgs e)
        {
            _spotify.Skip();
        }

        private static String FormatTime(double sec)
        {
            var span = TimeSpan.FromSeconds(sec);
            String secs = span.Seconds.ToString(), mins = span.Minutes.ToString();
            if (secs.Length < 2)
                secs = "0" + secs;
            return mins + ":" + secs;
        }
      
        private void record_Click(object sender, EventArgs e)
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                RecordBtn.Text = "Stop Rec";
            }
            else
            {
                StopRecording();
                RecordBtn.Text = "Record";
            }            
        }

        private void RecordCurrentTrack()
        {
            var fileName =  $"{_currentTrack.ArtistResource.Name} - {_currentTrack.AlbumResource.Name} - {_currentTrack.TrackResource.Name}.mp3";
            //settings.IsEnabled = false;
            //rewindTrack();
            //await spotify.Pause().ConfigureAwait(false);
            var outputFile = $@"{RecordPathTextBox.Text}\{fileName}";
            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
            
            const int deviceId = 4; //TODO: move into view

            var remainingTimeOnCurrentTrack = _currentTrack.Length - timeProgressBar.Value;
            var until = $"--until={remainingTimeOnCurrentTrack/60}:{remainingTimeOnCurrentTrack%60:D2}";

            var metaData = $"--meta='artist={_currentTrack.ArtistResource.Name};title={_currentTrack.TrackResource.Name};album={_currentTrack.AlbumResource.Name};'";
            const string quality = "--mpeg-quality=320";
            var args = $@"--record --out ""{outputFile}"" --dev-loopback={deviceId} {until} {metaData} {quality}";
            Debug.WriteLine($"About to start recording with: {FMediaPath} {args}");
            var p = new ProcessStartInfo(FMediaPath, args)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = false
            };
            Task.Factory.StartNew(() =>
            {
                _recordingProcess = Process.Start(p);
            });
        }

        private static string FMediaPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib\\fmedia.exe");

        private void StopRecording()
        {
            var p = new ProcessStartInfo(FMediaPath, "--globcmd=quit")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = false
            };
            Task.Factory.StartNew(() =>
            {
                _recordingProcess = Process.Start(p);
            });

            //if (_recordingProcess == null || _recordingProcess.HasExited) return;

            //try
            //{
                

            //    _recordingProcess.Kill();
            //}
            //catch (Exception)
            //{
            //    Debug.WriteLine("Failed to kill recording process, most likely terminated");
            //}
        }
    }
}