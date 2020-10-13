using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine.UI;
using System.Threading;

public class UdpBroadcast : MonoBehaviour
{
    [SerializeField]
    private Text StatusUi;
    [SerializeField]
    private int port = 8888;
    [SerializeField]
    private InputField InputObj;

    private bool isController = true, isConnected = false, isBroadcasting = false;
    private string Message = "HELLO", ConfirmationMessage = "ClientConfirmed";

    public static string ReceivedMessage;

    //Event is called whenever a new message is received
    public delegate void MessageDelegate();
    public static MessageDelegate OnMessageReceived;

    UdpClient peer;
    IPEndPoint BroadcastEndpoint,AnyEndPoint, OtherPeer;    
    IPAddress localIp;

    
    Queue<string> MessageQueue;

    //Thread used to recevie messages
    Thread t;
    //Allows app to receive broadcasted messages
    AndroidJavaObject multicastLock;

    void Start()
    {
        GetLocalIPAddress();

        //Local device client initialization
        peer = new UdpClient();
        peer.EnableBroadcast = true;
        peer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        peer.ExclusiveAddressUse = false;
        peer.MulticastLoopback = false;
        #if UNITY_ANDROID              
            MulticastLock();
        #endif
        peer.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        //Endpoint initialization
        BroadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, port);
        AnyEndPoint = new IPEndPoint(IPAddress.Any, port);

        OtherPeer = null;
        OnMessageReceived += TryEstablishConnection;
        MessageQueue = new Queue<string>();
    }


    //Call this function to change message used to establish connection
    public void SetMessage(string msg)
    {
        Message = msg;
    }

    /// <summary>
    ///     Function sets this device's type and starts handshake procedure
    /// </summary>
    /// <param name="type">Set true if this device is to be started as server</param>
    public void StartPeer(bool type)
    {
        StatusUi.gameObject.SetActive(true);
        StatusUi.text = "Connecting";
        ConfirmationMessage += Message;
        Debug.Log(ConfirmationMessage);
        isController = type;
        t = new Thread(()=>ReceiveMessage(AnyEndPoint))
        {
            IsBackground = true
        };
        t.Start();
        isBroadcasting = true;
    }

    /// <summary>
    /// Called by device once other peer is detected
    /// </summary>
    public void ConnectToOtherPeer()
    {
        StatusUi.text = "Connected to other peer:"+OtherPeer.Address.ToString();
        if (!isController)
            StartCoroutine(SendConfirmation());
        t = new Thread(() => ReceiveMessage(OtherPeer))
        {
            IsBackground = true
        };
        t.Start();
        InputObj.gameObject.SetActive(true);
        OnMessageReceived += ShowText;
    }

    IEnumerator SendConfirmation()
    {
        int i = 0;
        while(i<10)
        {
            i++;
            SendUdpMessage(ConfirmationMessage);
            StatusUi.text = i.ToString();
            yield return 0;
        }
    }
     
    //Function is subscribed to OnMessageReveived after handshake
    private void ShowText()
    {
        StatusUi.text = ReceivedMessage;
    }

    private void Update()
    {        
        if (MessageQueue.Count > 0)
        {
            ReceivedMessage = MessageQueue.Dequeue();
            OnMessageReceived?.Invoke();
        }        
    }

    //verifies handshake
    private void TryEstablishConnection()
    {
        var data = ReceivedMessage.Split(',');
        if ((!isController && data[0] == Message) || (isController && data[0] == ConfirmationMessage))
        {
            isConnected = true;
            isBroadcasting = false;
            OnMessageReceived -= TryEstablishConnection;
            OtherPeer = new IPEndPoint(IPAddress.Parse(data[1]), port);
            if (t != null)
                t.Abort();
            ConnectToOtherPeer();
            StatusUi.text = "Connecting to " + data[1];
        }
        else
            StatusUi.text = ReceivedMessage;
    }

    //Called by send button when message is to be sent to peer
    public void SendMessageCallback()
    {
        SendUdpMessage(InputObj.text);
        InputObj.text = "";
    }

    /// <summary>
    /// Sends message using UDP to other peer after handshake
    /// </summary>
    /// <param name="msg">Message to send</param>
    private void SendUdpMessage(string msg)
    {
        byte[] data = Encoding.ASCII.GetBytes(msg);
        peer.Send(data, data.Length, OtherPeer);
    }

    //Called as part of thread since peer.receive() is a blocking function
    void ReceiveMessage(IPEndPoint ep)
    {
        while (true)
            {
                if (isController && isBroadcasting)
                {
                    byte[] data = Encoding.ASCII.GetBytes(Message);
                    peer.Send(data, data.Length, BroadcastEndpoint);
                }
                try
                    {                    
                    byte[] data = peer.Receive(ref ep);
                    if (data != null && data.Length > 0 )
                    {
                        string txt = Encoding.ASCII.GetString(data);
                        txt =  txt + "," + ep.Address.ToString(); 
                        lock (MessageQueue)
                        {
                            if (ep.Address.ToString() != localIp.ToString())
                                MessageQueue.Enqueue(txt);
                        }
                    }
                    
                }
                catch (Exception e)
                {                    
                    Debug.Log(e);
                }                
                Thread.Sleep(10);
            }     
      
    }

    void MulticastLock()
    {
        using (AndroidJavaObject activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity"))
        {
            using (var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "lock");
                multicastLock.Call("acquire");
            }
        }
    }

    private void OnDestroy()
    {
        if (t != null)
            t.Abort();
        peer.Close();
        multicastLock?.Call("release");
    }

    private void OnApplicationQuit()
    {
        if (t != null)
            t.Abort();
        peer.Close();
        multicastLock?.Call("release");
        OnMessageReceived -= ShowText;
    }

    public void GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)            
                localIp = ip;            
        }       
    }

}
