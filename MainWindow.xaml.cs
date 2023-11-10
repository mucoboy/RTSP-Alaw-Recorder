using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

namespace RTSP_Recorder
{
    //implement INotify interface to notify user
    public partial class MainWindow : Window, INotify
    {
        WaveOut waveOut;
        BufferedWaveProvider bwp;
        Recorder recorder;
        ObservableCollection<string> clientList;
        ObservableCollection<RecordItem> recordList;
        object clientLock = new object();//for thread safety of client list

        public MainWindow()
        {
            InitializeComponent();
            
            //audio output settings
            waveOut = new WaveOut();
            waveOut.Volume = 1;
            bwp = new BufferedWaveProvider(new WaveFormat(8000,16,1));
            bwp.BufferLength = 10_000_000;
            bwp.DiscardOnBufferOverflow = true;
            waveOut.Init(bwp);
            waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
            waveOut.Play();

            clientList = new ObservableCollection<string>();
            recordList = new ObservableCollection<RecordItem>();
            clientListBox.ItemsSource = clientList;
            getFiles();
            recordingsDataGrid.ItemsSource = recordList;
            recorder = new Recorder(this);//create recorder object
            liveCheckBox.IsChecked = true;
 
        }

        #region Methods
        void getFiles()
        {
            try
            {

                foreach (var item in Directory.GetFiles(Environment.CurrentDirectory + "/iprecorder"))
                {

                    //if there is a invalid file, ignore it
                    try
                    {

                        var items = item.Substring(item.IndexOf("iprecorder")).Remove(0, 11).Replace(".wav", "").Split('_');
                        var ID = Convert.ToInt32(items[0]);
                        var recordItem = new RecordItem
                        {
                            ID = ID,
                            Source = items[1],
                            From = items[2],
                            To = items[3],
                        };

                        recordList.Add(recordItem);
                        if (Recorder.ID < ID)
                            Recorder.ID = ID;



                    }
                    catch (Exception)
                    {


                    }


                }

                //sort by id number
                if (recordList.Count > 0)
                {
                    for (int i = 0; i < recordList.Count; i++)
                    {
                        var smallest = recordList[i];
                        for (int j = i; j < recordList.Count; j++)
                        {
                            if (smallest.ID > recordList[j].ID)
                                smallest = recordList[j];
                        }
                        recordList.Remove(smallest);
                        recordList.Insert(i, smallest);
                    }

                    recordingsDataGrid.ScrollIntoView(recordList[recordList.Count - 1]);
                }
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString());
            }
        }
        #endregion

        #region Events
       
        //when playback stopped, reset values
        private void WaveOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            playButton.IsChecked = false;
            soundProgress.Value = 1;
        }

        //start or stop recorder
        private void startStopButton_Click(object sender, RoutedEventArgs e)
        {
            startStopButton.IsChecked = !startStopButton.IsChecked;

            if (startStopButton.IsChecked == false)
            {
                if (ushort.TryParse(portTextBox.Text, out ushort port))
                    recorder.start(port);
                else
                    MessageBox.Show("Invalid port number");
            }

            else
                recorder.stop();

        }

        //play or stop a recording.
        private void playButton_Click(object sender, RoutedEventArgs e)
        {
            playButton.IsChecked = !playButton.IsChecked;

            if (playButton.IsChecked == false && recordingsDataGrid.SelectedIndex >= 0)
            {
                var recordItem = recordingsDataGrid.SelectedItem as RecordItem;
                var path = Environment.CurrentDirectory + "/iprecorder/" + recordItem.ID + "_" + recordItem.Source + "_" + recordItem.From + "_" + recordItem.To + ".wav";
                play(path);
                playButton.IsChecked = true;

            }

            //if recording not selected, notify user
            else if (playButton.IsChecked == false)
                MessageBox.Show("Select a Recording!");

            else
            {
                playButton.IsChecked = false;
                if (waveOut.PlaybackState == PlaybackState.Playing)
                    waveOut.Stop();
            }

        }

        //play a recording in new thread
        void play(string path)
        {
            new Thread(() => {

                try
                {
                    bwp.ClearBuffer();
                    if (waveOut.PlaybackState != PlaybackState.Playing)
                        waveOut.Play();
                    var buffer = new byte[10_000_000];
                    var length = 0;
                    
                    //get the record from /iprecorder path
                    using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        length = fileStream.Read(buffer, 0, buffer.Length);
                    }

                    bwp.AddSamples(buffer, 44, length - 44);

                    var duration = length / 16;

                    var stopWatch = new Stopwatch();
                    stopWatch.Start();

                    while (stopWatch.ElapsedMilliseconds<duration)
                    {
                        soundProgress.Dispatcher.Invoke(() => soundProgress.Value = (double)(buffer[stopWatch.ElapsedMilliseconds] / 2.5));

                        Thread.Sleep(20);

                        //if playback stopped, terminate this thread
                        if (waveOut.PlaybackState != PlaybackState.Playing) return;
                    }


                    if (waveOut.PlaybackState == PlaybackState.Playing)
                        waveOut.Stop();


                }
                catch (Exception)
                {

                    if (waveOut.PlaybackState == PlaybackState.Playing)
                        waveOut.Stop();
                }
            }).Start();
        }

        //update content of play button
        private void playButton_Checked(object sender, RoutedEventArgs e)
        {
            playButton.Content = "Pause";
        }

        private void playButton_Unchecked(object sender, RoutedEventArgs e)
        {
            playButton.Content = "Play";
        }

        //start live broadcast
        private void liveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    waveOut.Stop();
                    bwp.ClearBuffer();
                }
                playButton.IsChecked = false;
                playButton.IsEnabled = false;
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString());
            }
        }

        //enable play button
        private void liveCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    waveOut.Stop();
                    bwp.ClearBuffer();
                }
                playButton.IsChecked = false;
                playButton.IsEnabled = true;
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString());
            }
        }

        //before closing, release all resources
        private void Window_Closed(object sender, EventArgs e)
        {
            recorder.stop();
            Process.GetCurrentProcess().Kill();
        }
        #endregion


        #region INotify implementation

        //when a client connected, add it to list
        public void onClientConnected(string clientName)
        {
            try
            {
                lock (clientLock)
                {
                    clientListBox.Dispatcher.Invoke(() => clientList.Add(clientName));
                }
            }
            catch (Exception)
            {


            }
        }

        //when a client disconnected, remove it from list
        public void onClientDisconnected(string clientName)
        {
            try
            {
                lock (clientLock)
                {
                    clientListBox.Dispatcher.Invoke(() => clientList.Remove(clientName));
                }
            }
            catch (Exception)
            {


            }
        }

        //when data received, add it to buffer and update progress bar
        public void onDataReceived(byte[] data)
        {
            try
            {
                Dispatcher.Invoke(() => {
                    if (liveCheckBox.IsChecked == true)
                    {
                        if (waveOut.PlaybackState != PlaybackState.Playing)
                            waveOut.Play();
                        bwp.AddSamples(data, 0, data.Length);
                    }

                    soundProgress.Value = (double)(data[0] / 2.5);
                });
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString());
            }

        }

        //on listener started, update button and label
        public void onListenerStarted()
        {
            Dispatcher.Invoke(() => {

                startStopButton.IsChecked = true;
                startStopButton.Content = "Stop";
                listenerStatusLabel.Content = "Recording started!";
                listenerStatusLabel.Foreground = Brushes.Green;
            });
        }

        
        //if an exception occured, notify user
        public void onListenerStopped(string cause)
        {
            Dispatcher.Invoke(() => {
                startStopButton.IsChecked = false;
                startStopButton.Content = "Start";
                listenerStatusLabel.Content = "Press Start for Recording!";
                listenerStatusLabel.Foreground = Brushes.Red;
                soundProgress.Value = 1;

            });

            if (cause != "user action")
                MessageBox.Show("Fault: " + cause);
        }

        //add recording to list
        public void onRecordingStopped(RecordItem recordItem)
        {
            Dispatcher.Invoke(() => {
                recordList.Add(recordItem);
                soundProgress.Value = 1;
                recordingsDataGrid.ScrollIntoView(recordList[recordList.Count - 1]);
            });
        }


        #endregion

        
    }
}
