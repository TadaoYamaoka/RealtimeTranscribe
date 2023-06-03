using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RealtimeTranscribe
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Whisper? whisper;
        WasapiCapture? capture;
        BufferedWaveProvider? bufferedWaveProvider;
        DispatcherTimer? timer;

        public MainWindow()
        {
            InitializeComponent();
        }

        void InitCaptuer()
        {
            bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat);
            bufferedWaveProvider.DiscardOnBufferOverflow = true;
            bufferedWaveProvider.ReadFully = false;

            capture.DataAvailable += (s, a) =>
            {
                bufferedWaveProvider.AddSamples(a.Buffer, 0, a.BytesRecorded);
            };

            capture.RecordingStopped += (s, a) =>
            {
                capture.Dispose();
                capture = null;
            };
        }

        void InitWasapiCapture()
        {
            capture = new WasapiCapture();
            InitCaptuer();
        }

        void InitWasapiLoopbackCapture()
        {
            capture = new WasapiLoopbackCapture();
            InitCaptuer();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitWasapiCapture();

            capture.StartRecording();

            whisper = new Whisper();

            timer = new DispatcherTimer();
            timer.Interval = new TimeSpan(0, 0, 3);
            timer.Tick += new EventHandler(MyTimerMethod);
            timer.Start();

            this.Closing += new CancelEventHandler((object? sender, CancelEventArgs e) => {
                timer.Stop();
                capture.StopRecording();
                whisper.Dispose();
            });
        }
        private void MyTimerMethod(object? sender, EventArgs e)
        {
            //System.Diagnostics.Debugger.Log(0, null, capture.CaptureState != CaptureState.Stopped ? "staring\n" : "stopped\n");

            var (language, result) = whisper.decode(bufferedWaveProvider.ToSampleProvider());

            if (language != null && result != null && result != "")
            {
                var textBlock = new TextBlock();
                textBlock.Text = language + ": " + result;
                textBlock.TextWrapping = TextWrapping.Wrap;
                this.listBox.Items.Add(textBlock);

                this.listBox.ScrollIntoView(this.listBox.Items.GetItemAt(this.listBox.Items.Count - 1));
            }
        }
        private void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            var callback = new DispatcherOperationCallback(ExitFrames);
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, callback, frame);
            Dispatcher.PushFrame(frame);
        }
        private object ExitFrames(object obj)
        {
            ((DispatcherFrame)obj).Continue = false;
            return null;
        }

        private void radioButtonMic_Checked(object sender, RoutedEventArgs e)
        {
            if (capture != null)
            {
                timer?.Stop();
                capture.StopRecording();
                while (capture != null)
                    DoEvents();
                InitWasapiCapture();
                capture.StartRecording();
                timer?.Start();
            }
        }

        private void radioButtonLoopback_Checked(object sender, RoutedEventArgs e)
        {
            if (capture != null)
            {
                timer?.Stop();
                capture.StopRecording();
                while (capture != null)
                    DoEvents();
                InitWasapiLoopbackCapture();
                capture.StartRecording();
                timer?.Start();
            }
        }

        private void button_Checked(object sender, RoutedEventArgs e)
        {
            if (capture != null && !timer.IsEnabled)
            {
                bufferedWaveProvider.ClearBuffer();
                timer?.Start();
            }
        }

        private void button_Unchecked(object sender, RoutedEventArgs e)
        {
            timer?.Stop();
        }
    }
}
