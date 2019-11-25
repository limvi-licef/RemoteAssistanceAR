// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MixedRemoteViewCompositor.Network;
using System.Diagnostics;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Input;
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;
using System.IO;

/**
 * Majority of the code developped by Guillaume Spalla.
 * Structure of the application is based on LowLatencyMRC project from Microsoft, as well as some pieces of code (https://github.com/Microsoft/MixedRealityCompanionKit/tree/master/MixedRemoteViewCompositor/Samples/LowLatencyMRC/UWP)
 */
namespace Viewer
{
    public sealed partial class Playback : Page
    {
        enum ANNOTATIONS { ANNOTATIONS_ARROW = 0, ANNOTATIONS_NOENTRY = 1, ANNOTATION_ARROWGUIDANCE_SHOW = 2, ANNOTATION_ARROWGUIDANCE_HIDE = 3 };
        enum DIRECTIONS { DIRECTIONS_LEFT = 0, DIRECTIONS_RIGHT = 1, DIRECTIONS_UP = 2, DIRECTIONS_DOWN = 3 };
        enum DEBUG { DEBUG_NO = 0, DEBUG_TEXT = 1, DEBUG_VISUAL = 2, DEBUG_TEXTANDVISUAL = 3 };

        private MediaExtensionManager mediaExtensionManager = null;

        private Connector connector = null;
        private Connection connection = null;

        private DataWriter m_output;
        public String m_sIPLocalUDP;
        public String m_sPortLocalUDP;
        public String m_sIPRemoteUDP;
        public String m_sPortRemoteUDP;
        public String m_sIPRemoteVideo;
        public String m_sPortRemoteVideo;

        private int m_iButtonNoEntryStatus;
        private int m_iButtonArrowStatus;
        private int m_iButtonArrowGuidanceStatus;
        private int m_iDebugStatus;
        private bool m_bIsConnected;
        private DatagramSocket m_dsUdpSocket;

        // For Joystick
        enum JOYSTICKSTATUS { JOYSTICKSTATUS_STOPPED = 0, JOYSTICKSTATUS_INIT = 1, JOYSTICKSTATUS_ONGOING = 2};
        private JOYSTICKSTATUS m_joystickStatus;
        private double m_joystickInitX;
        private double m_joystickInitY;

        public Playback()
        {
            this.InitializeComponent();
            this.PointerReleased += Viewer_PointerReleased;
            this.PointerPressed += Viewer_PointerPressed;
            this.PointerMoved += Viewer_PointerMoved;
            this.PointerWheelChanged += Viewer_PointerWheelChanged;

            m_iButtonArrowGuidanceStatus = 0;
            m_iButtonArrowStatus = 1;
            m_iButtonNoEntryStatus = 0;
            m_iDebugStatus = 0;
            m_bIsConnected = false;

            initializeGUI();
            start();

            ApplicationView.PreferredLaunchViewSize = new Windows.Foundation.Size(1280, 720+90);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

            Brush b = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            txNoHitsInfo.Foreground = b;

            // Joystick
            m_joystickStatus = JOYSTICKSTATUS.JOYSTICKSTATUS_STOPPED;
            m_joystickInitX = -1;
            m_joystickInitY = -1;
        }

        async void start()
        {
            Debug.WriteLine("[Playback::start] Waiting for a connection...");

            m_dsUdpSocket = new DatagramSocket();
            m_dsUdpSocket.MessageReceived += Socket_MessageReceived;

            // Initialization UDP communication
            try
            {
                m_sIPLocalUDP = "10.44.160.22";
                m_sPortLocalUDP = "62503";
                m_sIPRemoteUDP = "10.44.161.15";
                m_sPortRemoteUDP = "63777";
                await m_dsUdpSocket.ConnectAsync(new EndpointPair(new HostName(m_sIPLocalUDP), m_sPortLocalUDP, new HostName(m_sIPRemoteUDP), m_sPortRemoteUDP));
                m_output = new DataWriter(m_dsUdpSocket.OutputStream);
                Debug.WriteLine("[Playback::start] UDP connection initialization ok");
            }
            catch (Exception e)
            {
                Debug.WriteLine("[Playback::start] Error - UDP connection initialization ");
                return;
            }

            // Initialization video communication
            m_sIPRemoteVideo = m_sIPRemoteUDP;
            m_sPortRemoteVideo = "27772";
            videoPlayer.Visibility = Visibility.Collapsed;
        }

        //////////////////////////////////////////////////
        // Mouse controls
        //////////////////////////////////////////////////
        private void Viewer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            Pointer p = e.Pointer;
            PointerPoint pPoint = e.GetCurrentPoint(this);

            if (pPoint.Properties.IsMiddleButtonPressed)
            {
                
                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);
                Debug.Write("[Playback::Viewer_PointerMoved] " + pPoint.Position.ToString() + " | " + coordNorm[0] + " " + coordNorm[1] + "\n");
                sendUdpMessagePOS(coordNorm[0], coordNorm[1]);
            }
            else if (pPoint.Properties.IsLeftButtonPressed && m_iButtonArrowGuidanceStatus == 1 && m_joystickStatus == JOYSTICKSTATUS.JOYSTICKSTATUS_ONGOING)
            {
                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);
                double[] vInitX = new double[2];
                vInitX[0] = 1.0 - m_joystickInitX;
                vInitX[1] = 0.5 - m_joystickInitY;
                double[] vInitY = new double[2];
                vInitY[0] = 0.5 - m_joystickInitX;
                vInitY[1] = 1.0 - m_joystickInitY;
                double[] vNew = new double[2];
                vNew[0] = coordNorm[0] - m_joystickInitX;
                vNew[1] = coordNorm[1] - m_joystickInitY;

                double vInitXL = Math.Sqrt(Math.Pow(vInitX[0], 2) + Math.Pow(vInitX[1], 2));
                double vInitYL = Math.Sqrt(Math.Pow(vInitY[0], 2) + Math.Pow(vInitY[1], 2));
                double vNewL = Math.Sqrt(Math.Pow(vNew[0], 2) + Math.Pow(vNew[1], 2));

                double dotProductX = vInitX[0] * vNew[0] + vInitX[1] * vNew[1];
                double dotProductY = vInitY[0] * vNew[0] + vInitY[1] * vNew[1];

                double angleX = (Math.Acos( dotProductX / (vInitXL * vNewL)) * 180) / Math.PI;
                double angleY = (Math.Acos(dotProductY / (vInitYL * vNewL)) * 180) / Math.PI;

                //Debug.Write(angleX + " " + angleY +  " ");

                if ( angleX < 45 )
                {
                    //Debug.Write("Right");
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_RIGHT);
                }
                else if ( angleX > 135)
                {
                    //Debug.Write("Left");
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_LEFT);
                }
                else if ( angleY < 45)
                {
                    //Debug.Write("Top");
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_UP);
                }
                else if ( angleY > 135)
                {
                    //Debug.Write("Down");
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_DOWN);
                }

                //Debug.WriteLine("");
            }
        }

        private void Viewer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (m_iButtonArrowStatus == 1 || m_iButtonNoEntryStatus == 1)
            {
                Pointer p = e.Pointer;
                PointerPoint pPoint = e.GetCurrentPoint(this);

                Debug.Write(pPoint.Position.ToString() + "\n");

                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);

                if (coordNorm[0] >= 0 && coordNorm[0] <= 1 && coordNorm[1] >= 0 && coordNorm[1] <= 1)
                {
                    sendUdpMessagePOS(coordNorm[0], coordNorm[1]);
                }
                else
                {
                    Debug.Write(pPoint.Position.ToString() + "\t No msg sent \n");
                }
            }
            else if (m_iButtonArrowGuidanceStatus == 1 && m_joystickStatus == JOYSTICKSTATUS.JOYSTICKSTATUS_ONGOING)
            {
                m_joystickStatus = JOYSTICKSTATUS.JOYSTICKSTATUS_STOPPED;
            }
        }

        private void Viewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (m_iButtonArrowGuidanceStatus == 1 && Settings.m_useJoystick == false)
            {
                PointerPoint pPoint = e.GetCurrentPoint(this);
                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);

                if (coordNorm[0] < 0.35 && coordNorm[1] < 0.7 && coordNorm[1] > 0.3)
                {
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_LEFT);
                }
                else if (coordNorm[0] > 0.65 && coordNorm[1] < 0.7 && coordNorm[1] > 0.3)
                {
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_RIGHT);
                }
                else if (coordNorm[0] > 0.35 && coordNorm[0] < 0.65 && coordNorm[1] > 0.5)
                {
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_UP);
                }
                else if (coordNorm[0] > 0.35 && coordNorm[0] < 0.65 && coordNorm[1] < 0.3)
                {
                    sendUdpMessageNAV(DIRECTIONS.DIRECTIONS_DOWN);
                }
            }
            else if (m_iButtonArrowGuidanceStatus == 1 && Settings.m_useJoystick == true && m_joystickStatus == JOYSTICKSTATUS.JOYSTICKSTATUS_STOPPED)
            {
                PointerPoint pPoint = e.GetCurrentPoint(this);
                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);
                m_joystickInitX = coordNorm[0];
                m_joystickInitY = coordNorm[1];
                m_joystickStatus = JOYSTICKSTATUS.JOYSTICKSTATUS_ONGOING;

            }
        }

        private void Viewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint pPoint = e.GetCurrentPoint(this);
            PointerPointProperties pPointProperties = pPoint.Properties;
            
            if (pPointProperties.MouseWheelDelta>0)
            {
                if (m_iButtonArrowStatus == 1)
                {
                    m_iButtonArrowStatus = 0;
                    m_iButtonNoEntryStatus = 1;
                    m_iButtonArrowGuidanceStatus = 0;

                    changeButtonBackground(bnArrow, "target_Unselected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Selected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_NOENTRY);
                }
                else if (m_iButtonNoEntryStatus == 1)
                {
                    m_iButtonArrowStatus = 0;
                    m_iButtonNoEntryStatus = 0;
                    m_iButtonArrowGuidanceStatus = 1;

                    changeButtonBackground(bnArrow, "target_Unselected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Selected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_SHOW);
                }
                else if (m_iButtonArrowGuidanceStatus == 1)
                {
                    m_iButtonArrowStatus = 1;
                    m_iButtonNoEntryStatus = 0;
                    m_iButtonArrowGuidanceStatus = 0;

                    changeButtonBackground(bnArrow, "target_Selected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_HIDE);
                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_ARROW);
                }
            }
            else if (pPointProperties.MouseWheelDelta < 0)
            {
                if (m_iButtonArrowStatus == 1)
                {
                    m_iButtonArrowStatus = 0;
                    m_iButtonNoEntryStatus = 0;
                    m_iButtonArrowGuidanceStatus = 1;

                    changeButtonBackground(bnArrow, "target_Unselected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Selected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_SHOW);
                }
                else if (m_iButtonNoEntryStatus == 1)
                {
                    m_iButtonArrowStatus = 1;
                    m_iButtonNoEntryStatus = 0;
                    m_iButtonArrowGuidanceStatus = 0;

                    changeButtonBackground(bnArrow, "target_Selected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_ARROW);
                }
                else if (m_iButtonArrowGuidanceStatus == 1)
                {
                    m_iButtonArrowStatus = 0;
                    m_iButtonNoEntryStatus = 1;
                    m_iButtonArrowGuidanceStatus = 0;

                    changeButtonBackground(bnArrow, "target_Unselected.png");
                    changeButtonBackground(bnNoEntry, "noEntry_Selected.png");
                    changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_HIDE);
                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_NOENTRY);
                }
            }
                
        }

        //////////////////////////////////////////////////
        // GUI Controls
        //////////////////////////////////////////////////

        /*private async void bnConnect_Click(object sender, RoutedEventArgs e)
        {
            ushort port = 0;
            if (UInt16.TryParse(this.txPort.Text, out port))
            {
                if (string.IsNullOrEmpty(this.txAddress.Text))
                {
                    this.txAddress.Text = this.txAddress.PlaceholderText;
                }

                bool connectionStatus = await connect(this.txAddress.Text, port);
                if (connectionStatus)
                {
                    connectionSuccessful();
                }
            }
        }*/

        private void initializeGUI()
        {
            if (Settings.m_fullFeatures == false)
            {
                bnArrowGuidance.Opacity = 0;
                bnArrowGuidance.IsHitTestVisible = false;
                //bnArrowGuidance.Focus(FocusState.Unfocused);
                tbMessage.Opacity = 0;
                tbMessage.IsHitTestVisible = false;

                bnSendMessage.Opacity = 0;
                bnSendMessage.IsHitTestVisible = false;

                bnDebug.Opacity = 0;
                bnDebug.IsHitTestVisible = false;
            }
        }

        private void bnArrow_Click(object sender, RoutedEventArgs e)
        {
            if (m_iButtonArrowStatus == 0)
            {
                if (m_iButtonArrowGuidanceStatus == 1)
                {
                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_HIDE);
                }

                m_iButtonArrowStatus = 1;
                m_iButtonNoEntryStatus = 0;
                m_iButtonArrowGuidanceStatus = 0;

                changeButtonBackground((Button)sender, "target_Selected.png");
                changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_ARROW);
            }
        }

        private void bnNoEntry_Click(object sender, RoutedEventArgs e)
        {
            if (m_iButtonNoEntryStatus == 0)
            {
                if (m_iButtonArrowGuidanceStatus == 1)
                {
                    sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_HIDE);
                }

                m_iButtonArrowStatus = 0;
                m_iButtonNoEntryStatus = 1;
                m_iButtonArrowGuidanceStatus = 0;

                changeButtonBackground(bnArrow, "target_Unselected.png");
                changeButtonBackground(bnNoEntry, "noEntry_Selected.png");
                changeButtonBackground(bnArrowGuidance, "arrowGuide_Unselected.png");

                sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATIONS_NOENTRY);
            }
        }

        private void changeButtonBackground(Button b, string imagePath)
        {
            string path = "ms-appx:///Assets/"+imagePath;

            ImageBrush ib = new ImageBrush();
            ib.ImageSource = new BitmapImage(new Uri(path));

            b.Background = ib;
        }

        /*private void bnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseConnection();
        }*/

        /*private void bnStart_Click(object sender, RoutedEventArgs e)
        {
            StartPlayback(this.txAddress.Text, this.txPort.Text);
        }

        private void bnStop_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }*/

        private void bnArrowGuidance_Click(object sender, RoutedEventArgs e)
        {
            if (m_iButtonArrowGuidanceStatus == 0)
            {
                m_iButtonArrowStatus = 0;
                m_iButtonNoEntryStatus = 0;
                m_iButtonArrowGuidanceStatus = 1;

                changeButtonBackground(bnArrow, "target_Unselected.png");
                changeButtonBackground(bnNoEntry, "noEntry_Unselected.png");
                changeButtonBackground(bnArrowGuidance, "arrowGuide_Selected.png");

                sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_SHOW);
            }
        }

        private void bnDebug_Click(object sender, RoutedEventArgs e)
        {
            Button b = (Button)sender;
            if (m_iDebugStatus == 0)
            {
                m_iDebugStatus = 3;
                changeButtonBackground(b, "debugInfo_Selected.png");
                sendUdpMessageDEBUG(DEBUG.DEBUG_TEXTANDVISUAL);
            }    
            else if (m_iDebugStatus == 3)
            {
                m_iDebugStatus = 0;
                changeButtonBackground(b, "debugInfo_Unselected.png");
                sendUdpMessageDEBUG(DEBUG.DEBUG_NO);
            }
        }

        private void bnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            sendUdpMessageMESSAGE(tbMessage.Text);
        }

        //////////////////////////////////////////////////
        // Video stream connection and management
        //////////////////////////////////////////////////

        private void StartPlayback(string ip, string port)
        {
            this.videoPlayer.Source = new Uri(string.Format("mrvc://{0}:{1}", ip, port));
        }

        private async void StopPlayback()
        {
            await videoPlayer.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //videoPlayer.Visibility = Visibility.Visible;
                this.videoPlayer.Stop();
                this.videoPlayer.Source = null;
            });
           
        }

        /*private void connectionSuccessful()
        {
            this.bnConnect.IsEnabled = false;
            this.bnClose.IsEnabled = true;
            this.bnStart.IsEnabled = true;
            this.bnStop.IsEnabled = false;
        }*/

        private async Task<bool> connect(string ip, ushort port)
        {
            bool toReturn = false;

            this.connector = new Connector(ip, port);
            if (this.connector != null)
            {
                this.connection = await this.connector.ConnectAsync();
                if (this.connection != null)
                {
                    toReturn = true;

                    this.connection.Disconnected += Connection_Disconnected;

                    var propertySet = new PropertySet();
                    var propertySetDictionary = propertySet as IDictionary<string, object>;
                    propertySet["Connection"] = this.connection;

                    RegisterSchemeHandler(propertySet);
                }
            }

            return toReturn;
        }

        //////////////////////////////////////////////////
        // UDP messages
        //////////////////////////////////////////////////

        private void sendUdpMessageANNOTATION_SELECTION(ANNOTATIONS a)
        {
            string msgToSend = "ANNOTATION SELECT ";

            switch (a)
            {
                case ANNOTATIONS.ANNOTATIONS_ARROW:
                    msgToSend += "Arrow";
                    break;
                case ANNOTATIONS.ANNOTATIONS_NOENTRY:
                    msgToSend += "NoEntry";
                    break;
                case ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_SHOW:
                    msgToSend += "ArrowGuidance Show";
                    break;
                case ANNOTATIONS.ANNOTATION_ARROWGUIDANCE_HIDE:
                    msgToSend += "ArrowGuidance Hide";
                    break;
            }

            sendUdpMessage(msgToSend);
        }

        private void sendUdpMessagePOS(double x, double y)
        {
            string msgToSend = "POS x=" + x + " y=" + y;
            sendUdpMessage(msgToSend);
        }

        private void sendUdpMessageNAV(DIRECTIONS d)
        {
           string msgToSend = "NAV ";
            switch (d)
            {
                case DIRECTIONS.DIRECTIONS_LEFT:
                    msgToSend += "left";
                    break;
                case DIRECTIONS.DIRECTIONS_RIGHT:
                    msgToSend += "right";
                    break;
                case DIRECTIONS.DIRECTIONS_UP:
                    msgToSend += "up";
                    break;
                case DIRECTIONS.DIRECTIONS_DOWN:
                    msgToSend += "down";
                    break;
            }
            sendUdpMessage(msgToSend);
        }

        private void sendUdpMessageDEBUG(DEBUG level)
        {
            string msgToSend = "DEBUG ";
            switch (level)
            {
                case DEBUG.DEBUG_NO:
                    msgToSend += "no";
                    break;
                case DEBUG.DEBUG_TEXT:
                    msgToSend += "text";
                    break;
                case DEBUG.DEBUG_VISUAL:
                    msgToSend += "visual";
                    break;
                case DEBUG.DEBUG_TEXTANDVISUAL:
                    msgToSend += "textAndVisual";
                    break;
            }
            sendUdpMessage(msgToSend);
        }

        private void sendUdpMessageMESSAGE(string message)
        {
            string msgToSend = "MESSAGE " + message;
            sendUdpMessage(msgToSend);
        }

        private async void sendUdpMessage(string message)
        {
            Debug.WriteLine("[Playback::sendUdpMessage] Try to send message: " + message);
            m_output.WriteString(message);

            try
            {
                await m_output.StoreAsync();
            }
            catch (Exception exception)
            {
                Debug.WriteLine("[Playback::sendUdpMessage] Send failed with error: " + exception.Message);
                Debug.WriteLine("[Playback::sendUdpMessage] Trying to reconnect");
                try
                {
                    m_sIPLocalUDP = "192.168.137.1";
                    m_sPortLocalUDP = "62503";
                    m_sIPRemoteUDP = "192.168.137.161";
                    m_sPortRemoteUDP = "63777";
                    await m_dsUdpSocket.ConnectAsync(new EndpointPair(new HostName(m_sIPLocalUDP), m_sPortLocalUDP, new HostName(m_sIPRemoteUDP), m_sPortRemoteUDP));
                    m_output = new DataWriter(m_dsUdpSocket.OutputStream);
                    Debug.WriteLine("[Playback::start] UDP connection initialization ok");
                }
                catch (Exception e)
                {
                    Debug.WriteLine("[Playback::start] Error - UDP connection initialization ");
                    return;
                }
            }
        }

        private async void Socket_MessageReceived(Windows.Networking.Sockets.DatagramSocket sender,
        Windows.Networking.Sockets.DatagramSocketMessageReceivedEventArgs args)
        {
            Stream streamIn = args.GetDataStream().AsStreamForRead();
            StreamReader reader = new StreamReader(streamIn);
            string message = await reader.ReadLineAsync();

            Debug.WriteLine("[Playback::Socket_MessageReceived] " + message);

            if ( message == "CALL start")
            {
                if (m_bIsConnected == false)
                {
                    ushort usPortRemotVideo;
                    ushort.TryParse(m_sPortRemoteVideo, out usPortRemotVideo);
                    bool statusConnection = await connect(m_sIPRemoteVideo, usPortRemotVideo);
                    if (statusConnection)
                    {
                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            StartPlayback(m_sIPRemoteVideo, m_sPortRemoteVideo);
                        });
                        await videoPlayer.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            videoPlayer.Visibility = Visibility.Visible;
                        });
                        m_bIsConnected = true;
                    }
                }
            }
            else if (message == "WARNING noHits")
            {
                await txNoHitsInfo.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Brush b = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    txNoHitsInfo.Foreground = b;

                });
                

                // That way is not really recommended by Microsoft, but ... it seems to work.
                await Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith( task =>
                {
                      txNoHitsInfo.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        Brush b = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                        txNoHitsInfo.Foreground = b;

                    });
                });
            }
            
        }

        //////////////////////////////////////////////////
        // Others
        //////////////////////////////////////////////////

        private void Connection_Disconnected(Connection sender)
        {
            CloseConnection();
        }

        private async void CloseConnection()
        {
            StopPlayback();

            /*this.bnStart.IsEnabled = false;

            this.bnConnect.IsEnabled = true;
            this.bnClose.IsEnabled = false;*/

            if (this.connection != null)
            {
                this.connection.Disconnected -= Connection_Disconnected;
                this.connection.Uninitialize();
                this.connection = null;
            }

            if (this.connector != null)
            {
                this.connector.Uninitialize();
                this.connector = null;
            }

            await videoPlayer.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                videoPlayer.Visibility = Visibility.Collapsed;
            });

            m_bIsConnected = false;
        }

        public void RegisterSchemeHandler(PropertySet propertySet)
        {
            if (this.mediaExtensionManager == null)
            {
                this.mediaExtensionManager = new MediaExtensionManager();
            }

            this.mediaExtensionManager.RegisterSchemeHandler("MixedRemoteViewCompositor.Media.MrvcSchemeHandler", "mrvc:", propertySet);
        }

        double[] getNormalizedCoodinates(double x, double y)
        {
            double[] toReturn = new double[2];

            toReturn[0] = x / 1280.0;
            toReturn[1] = Math.Abs(((y - 90) / (810 - 90)) - 1);

            double fovScalingFactor = 0.1;
            toReturn[0] = toReturn[0]*(1-2*fovScalingFactor)+fovScalingFactor;
            toReturn[1] = toReturn[1] *(1-2*fovScalingFactor) + fovScalingFactor;

            return toReturn;

        }


    }
}
