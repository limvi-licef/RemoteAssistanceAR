﻿using UnityEngine;
using System;
using System.IO;

#if !UNITY_EDITOR
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.Storage.Streams;
#endif

public class UDPListener : MonoBehaviour
{
#if !UNITY_EDITOR
    DatagramSocket socket;
#endif

    public TextMesh m_tmUDPStatus;
    public String m_sIPLocal;
    public String m_sPortLocal;
    public String m_sIPRemote;
    public String m_sPortRemote;
    public GameObject m_directionalObject;
    private Vector3 m_vExecuteRotation;
    public Transform m_tCameraTransform;
    private Vector3 m_vCylinderOriginalPosition;
    public AnnotationsManager m_raycast;
    private int m_idebugStatus;

    private string m_sStatus = "";
#if !UNITY_EDITOR
    private DataWriter m_output;
#endif

    void Start()
    {
        Debug.Log("[UDPListener::Start] Called");
        //System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
        //customCulture.NumberFormat.NumberDecimalSeparator = ".";

        //System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

#if !UNITY_EDITOR
        
        m_sStatus = "Waiting for a connection...";

        socket = new DatagramSocket();
        socket.MessageReceived += Socket_MessageReceived;

        try
        {
            // await socket.ConnectAsync(new EndpointPair(new HostName(m_sIPLocal), m_sPortLocal, new HostName(m_sIPRemote), m_sPortRemote));
            var tsk = socket.ConnectAsync(new EndpointPair(new HostName(m_sIPLocal), m_sPortLocal, new HostName(m_sIPRemote), m_sPortRemote));
            m_output= new DataWriter(socket.OutputStream);    
            Debug.Log("exit start");
            m_sStatus = "UDP connection initialization ok";
        Debug.Log("[UDPListener::Start] UDP connection initialization ok");
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
            Debug.Log(SocketError.GetStatus(e.HResult).ToString());
            m_sStatus = "Error - UDP connection initialization ";
            return;
        }
#endif
        m_vExecuteRotation.x = 0;
        m_vExecuteRotation.y = 0;
        m_vExecuteRotation.z = 0;

        m_vCylinderOriginalPosition = m_directionalObject.transform.position;

        m_idebugStatus = 0;
        Debug.Log("[UDPListener::Start] End");
    }

    // Update is called once per frame
    void Update()
    {
        m_tmUDPStatus.text = m_sStatus;

        m_directionalObject.transform.Rotate(m_vExecuteRotation);
        
        m_vExecuteRotation.x = 0;
        m_vExecuteRotation.y = 0;
        m_vExecuteRotation.z = 0;

        if (m_idebugStatus == 0)
        {
            m_tmUDPStatus.gameObject.SetActive(false);
        }
        else if (m_idebugStatus == 3 || m_idebugStatus == 2)
        {
            m_tmUDPStatus.gameObject.SetActive(true);
        }
    }

    public void sendUdpMessage(string message)
    {
        Debug.Log("[UDPListener::sendUdpMessage] Called");
#if !UNITY_EDITOR
        m_output.WriteString(message);
       
        try
        {
            var tsk = m_output.StoreAsync();
            m_sStatus = "Message sent: " + message;
            Debug.Log("[UDPListener::sendUdpMessage] Message sent: " + message);
        }
        catch (Exception exception)
        {
            Debug.Log("[UDPListener::sendUdpMessage] Send failed with error: " + exception.Message);
            m_sStatus = "Error: Send message failed: " + exception.Message;
        }
#endif
        Debug.Log("[UDPListener::sendUdpMessage] End");
    }

    public void sendUdpMessage(byte[] message)
    {
#if !UNITY_EDITOR
        m_output.WriteBytes(message);
       
        try
        {
            var tsk =  m_output.StoreAsync();
            m_sStatus = "Message sent: " + message;
        }
        catch (Exception exception)
        {
            Debug.Log("[UDPListener::sendUdpMessage] Send failed with error: " + exception.Message);
            m_sStatus = "Error: Send message failed: " + exception.Message;
        }
#endif
    }


#if !UNITY_EDITOR
    private async void Socket_MessageReceived(Windows.Networking.Sockets.DatagramSocket sender,
        Windows.Networking.Sockets.DatagramSocketMessageReceivedEventArgs args)
    {
        Debug.Log("[UDPListener::Socket_MessageReceived] Called");
        //Read the message that was received from the UDP echo client.
        Stream streamIn = args.GetDataStream().AsStreamForRead();
        StreamReader reader = new StreamReader(streamIn);
        string message = await reader.ReadLineAsync();

        m_sStatus = "Message received: " + message;
        Debug.Log("[UDPListener::Socket_MessageReceived] Message received: " + message);

        if ( message.StartsWith("NAV") )
        {
            rotateCylinder(message);
        }
        else if (message.StartsWith("POS") )
        {
            performRaycast(message);
        }
        else if (message.StartsWith("ANNOTATION"))
        {
            processAnnotation(message);
        }
        else if (message.StartsWith("DEBUG"))
        {
            processDebug(message);
        }
        else if (message.StartsWith("MESSAGE"))
        {
            processMessage(message);
        }
        Debug.Log("[UDPListener::Socket_MessageReceived] End");
    }
#endif

    public void rotateCylinder(string message)
    {
        if (message.StartsWith("NAV ROTATE"))
        {
            if (message == "NAV ROTATE left")
            {
                m_vExecuteRotation.z = 10;
            }
            else if (message == "NAV ROTATE right")
            {
                m_vExecuteRotation.z = -10;
            }
            else if (message == "NAV ROTATE up")
            {
                m_vExecuteRotation.x = -10;
            }
            else if (message == "NAV ROTATE down")
            {
                m_vExecuteRotation.x = 10;
            }
        }
        else if ( message.StartsWith("NAV DIRECTION"))
        {
            string[] temp = message.Split('=');
            double x = double.Parse(temp[1].Split(' ')[0]);
            double y = double.Parse(temp[2]);
            m_raycast.changeDirectionArrow(x, y);
        }
           
    }

    public void performRaycast(string message)
    {
        string[] temp = message.Split('=');
        double x = double.Parse(temp[1].Split(' ')[0]);
        double y = double.Parse(temp[2]);

        m_raycast.addAnnotationRelativePos(x, y);
    }

    public void processAnnotation(string message)
    {
       if (message.StartsWith("ANNOTATION SELECT"))
        {
            if (message == "ANNOTATION SELECT Arrow")
            {
                m_raycast.changeAnnotation(0);
            }
            else if (message == "ANNOTATION SELECT NoEntry")
            {
                m_raycast.changeAnnotation(1);
            }
            else if (message == "ANNOTATION SELECT ArrowGuidance Show")
            {
                m_raycast.enableGuidanceArrow(true);
            }
            else if (message == "ANNOTATION SELECT ArrowGuidance Hide")
            {
                m_raycast.enableGuidanceArrow(false);
            }
        }
    }

    public void processDebug(string message)
    {
        if (message == "DEBUG no")
        {
            m_idebugStatus = 0;
            m_raycast.processDebug(0);
        }
        else if (message == "DEBUG textAndVisual")
        {
            m_idebugStatus = 3;
            m_raycast.processDebug(3);
        }
    }

   public void processMessage(string message)
    {
        string temp = message.Substring(8);

        m_raycast.addAnnotationMessage(temp);
    }
}