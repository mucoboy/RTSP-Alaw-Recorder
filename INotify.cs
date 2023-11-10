using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSP_Recorder
{
    //Interface between Recorder and GUI. Inform user when client connected, listener started etc.
    interface INotify
    {
        void onClientConnected(string clientName);
        void onClientDisconnected(string clientName);
        void onDataReceived(byte[] data);
        void onRecordingStopped(RecordItem recordItem);
        void onListenerStarted();
        void onListenerStopped(string cause);


    }
}
