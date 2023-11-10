//using g711audio;
using NAudio.Codecs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RTSP_Recorder
{
    
    class Recorder
    {
        public static int ID;
        TcpListener listener;
        INotify notify;//notify user when data received, client connected so on..
        List<TcpClient> clientList;
        object clientLock = new object();
        public Recorder(INotify notify)
        {
            this.notify = notify;
            clientList = new List<TcpClient>();
        }

        //Start listener in different thread
        public void start(int port)
        {
            new Thread(()=> {

                try
                {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    notify.onListenerStarted();
                    
                    //this loop stops when listener stops
                    while (true)
                    {

                        var client = listener.AcceptTcpClient();
                        var name = (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
                        notify.onClientConnected(name);
                        new Thread(() => listenToClient(client, name)).Start();//new Thread for every single client

                    }

                }

                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.Interrupted)
                        notify.onListenerStopped("user action");

                    else if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                        notify.onListenerStopped("Adres is already in use!");
                }
                catch (Exception ex)
                {

                    notify.onListenerStopped(ex.ToString());
                }

            }).Start();
        }

        //send terminate message to every client before listener stopped
        public void stop()
        {
            try
            {
                lock (clientLock)
                {
                    foreach (var item in clientList)
                    {
                        try
                        {
                            item.Close();
                        }
                        catch (Exception)
                        {

                            
                        }
                    }
                }
                if (listener != null)
                    listener.Stop();
            }
            catch (Exception)
            {

                
            }
        }

        //every client has a individual thread for session
        private void listenToClient(TcpClient client, string name)
        {
            var from = "";
            var to = "";
            var memoryStream = new MemoryStream();//add received data to memoryStream. when recording finished, write to file
            
            try
            {
                lock (clientLock)
                {
                    clientList.Add(client);
                }

                //random session ID for every session
                var sessionID = new Random().Next(1_000_000,int.MaxValue);
                
                client.ReceiveTimeout = 6000; //if we don't receive any data in 6 sec, stop this thread
                var stream = client.GetStream();

                var length = 0;
                var received = new byte[10000];

                while ((length = stream.Read(received,0,received.Length))>0)
                {
                    //rtsp session messages is splitted according to ASCII <CR> <LF> chars
                    var content = Encoding.UTF8.GetString(received,0,length).Split(new string[] { "\r\n"},StringSplitOptions.None);
                    if (content.Length>=2)
                    {
                        var cSeq = content[1];
                        var reply = Encoding.UTF8.GetBytes("RTSP/1.0 200 OK\r\n" + cSeq + "\r\nSession: " + sessionID.ToString() + "\r\nPublic: DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE, RECORD\r\n\r\n");
                        stream.Write(reply,0,reply.Length);
                        
                        //if record started, recreate memoryStream and update "from" value
                        if (content[0].Length>6 && content[0].Substring(0,6) == "RECORD")
                        {
                            from = DateTime.Now.ToLongTimeString().Replace(':','-');
                            memoryStream = new MemoryStream();
                        }

                        //if record stopped, save all data in file
                        if (content[0].Length > 5 && content[0].Substring(0, 5) == "PAUSE")
                        {
                            to = DateTime.Now.ToLongTimeString().Replace(':', '-');
                            var path = Environment.CurrentDirectory + "/iprecorder/" + (++ID).ToString() + "_" + name + "_" + from + "_" + to + ".wav";

                            saveFile(memoryStream,path);

                            notify.onRecordingStopped(new RecordItem
                            {
                                From = from,
                                To = to,
                                Source = name,
                                ID = ID
                            }); 
                            
                        }

                    }

                    //rtp data messages
                    else if(length>16)
                    {
                        //remove 16 bytes header
                        var data = new byte[length-16];
                        Array.Copy(received,16,data,0,data.Length);

                        ///short to byte
                        var decoded = new byte[data.Length * 2];
                        for (int i = 0; i < data.Length; i++)
                        {
                            var sample = ALawDecoder.ALawToLinearSample(data[i]);
                            decoded[i * 2] = (byte)sample;
                            decoded[i * 2 + 1] =  (byte)(sample >> 8);
                        }

                        memoryStream.Write(decoded, 0, decoded.Length);
                        
                        notify.onDataReceived(decoded);
                    }
                }

                notify.onClientDisconnected(name);
            }
            catch (Exception)
            {
                
                lock (clientLock)
                {
                    clientList.Remove(client);
                }
                notify.onClientDisconnected(name);

                //if client disconnected before pause, record last data
                if (memoryStream.Length > 0)
                {
                    to = DateTime.Now.ToLongTimeString().Replace(':', '-');
                    var path = Environment.CurrentDirectory + "/iprecorder/" + (++ID).ToString() + "_" + name + "_" + from + "_" + to + ".wav";

                    saveFile(memoryStream, path);
                    notify.onRecordingStopped(new RecordItem
                    {
                        From = from,
                        To = to,
                        Source = name,
                        ID = ID
                    });
                }
            }
        }

        //when pause message received, save file
        private void saveFile(MemoryStream memoryStream, string path)
        {
            try
            {

                using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    //44 bytes wav header + memoryStream data
                    using (var binaryWriter = new BinaryWriter(fileStream))
                    {

                        binaryWriter.Write(new char[4] { 'R', 'I', 'F', 'F' });
                        
                        binaryWriter.Write((int)memoryStream.Length - 8);

                        binaryWriter.Write(new char[8] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                        binaryWriter.Write(16);

                        binaryWriter.Write((short)1);
                        binaryWriter.Write((short)1);

                        binaryWriter.Write(8000);//sample rate


                        binaryWriter.Write(8000 * (16 * 1 / 8));//8000 sample rate, 16 bps, 1 channel,8 to byte

                        binaryWriter.Write((short)(16 * 1 / 8));

                        binaryWriter.Write((short)16);

                        binaryWriter.Write(new char[4] { 'd', 'a', 't', 'a' });
                        binaryWriter.Write((int)memoryStream.Length-44);
                        memoryStream.WriteTo(fileStream);
                    }

                    
                }

               
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.ToString());
            }
        }
    }
}
