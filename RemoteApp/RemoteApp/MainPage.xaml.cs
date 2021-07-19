using System;
using System.Collections.Generic;
using System.IO;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using Microsoft.MixedReality.WebRTC;
using System.Diagnostics;
using Windows.Media.Capture;
using Windows.ApplicationModel;
using TestAppUwp.Video;
using TestAppUwp;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.UI.Input;
using Windows.UI.Xaml.Media.Imaging;
using System.Threading.Tasks;

// Pour plus d'informations sur le modèle d'élément Page vierge, consultez la page https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace RemoteApp
{
    /// <summary>
    /// Une page vide peut être utilisée seule ou constituer une page de destination au sein d'un frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // For video streaming
        private PeerConnection _peerConnection;
        private MediaStreamSource _localVideoSource;
        private VideoBridge _localVideoBridge = new VideoBridge(3);
        private bool _localVideoPlaying = false;
        private object _localVideoLock = new object();
        private NodeDssSignaler _signaler;
        private object _remoteVideoLock = new object();
        private bool _remoteVideoPlaying = false;
        private MediaStreamSource _remoteVideoSource;
        private VideoBridge _remoteVideoBridge = new VideoBridge(5);

        // For interactions
        enum ANNOTATIONS { ANNOTATIONS_ARROW = 0, ANNOTATIONS_NOENTRY = 1, ANNOTATION_ARROWGUIDANCE_SHOW = 2, ANNOTATION_ARROWGUIDANCE_HIDE = 3 };
        enum DIRECTIONS { DIRECTIONS_LEFT = 0, DIRECTIONS_RIGHT = 1, DIRECTIONS_UP = 2, DIRECTIONS_DOWN = 3 };
        enum DEBUG { DEBUG_NO = 0, DEBUG_TEXT = 1, DEBUG_VISUAL = 2, DEBUG_TEXTANDVISUAL = 3 };

        private DataWriter m_output;
        
        private int m_iButtonNoEntryStatus;
        private int m_iButtonArrowStatus;
        private int m_iButtonArrowGuidanceStatus;
        private int m_iDebugStatus;
        private bool m_bIsConnected;
        private DatagramSocket m_dsUdpSocket;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            Application.Current.Suspending += App_Suspending;

            // Interactions
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

            ApplicationView.PreferredLaunchViewSize = new Windows.Foundation.Size(1280, 720 + 90);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

            Brush b = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            txNoHitsInfo.Foreground = b;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Request access to microphone and camera
            var settings = new MediaCaptureInitializationSettings();
            settings.StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo;
            var capture = new MediaCapture();
            await capture.InitializeAsync(settings);

            // Retrieve a list of available video capture devices (webcams).
            List<VideoCaptureDevice> deviceList =
                await PeerConnection.GetVideoCaptureDevicesAsync();

            // Get the device list and, for example, print them to the debugger console
            foreach (var device in deviceList)
            {
                // This message will show up in the Output window of Visual Studio
                Debugger.Log(0, "", $"Webcam {device.name} (id: {device.id})\n");
            }

            _peerConnection = new PeerConnection();


            var config = new PeerConnectionConfiguration
            {
                IceServers = new List<IceServer> {
            new IceServer{ Urls = { "stun:stun.l.google.com:19302" } }
        }
            };
            await _peerConnection.InitializeAsync(config);
            Debugger.Log(0, "", "Peer connection initialized successfully.\n");

            await _peerConnection.AddLocalAudioTrackAsync();

            if (Settings.m_showLocal)
            {
                //LocalVideoTrack _localVideoTrack;
                await _peerConnection.AddLocalVideoTrackAsync();
                _peerConnection.I420LocalVideoFrameReady += Peer_LocalI420AFrameReady;
                await _peerConnection.AddLocalAudioTrackAsync();

                

                remoteVideoPlayerElement.Visibility = Visibility.Visible;
            }
            else
            {
                _peerConnection.Connected += () =>
                {
                    Debugger.Log(0, "", "PeerConnection: connected.\n");
                };
                _peerConnection.IceStateChanged += (IceConnectionState newState) =>
                {
                    Debugger.Log(0, "", $"ICE state: {newState}\n");
                };

                _peerConnection.I420RemoteVideoFrameReady += Peer_RemoteI420AFrameReady;

                _peerConnection.LocalSdpReadytoSend += Peer_LocalSdpReadytoSend;
                _peerConnection.IceCandidateReadytoSend += Peer_IceCandidateReadytoSend;

                // Initialize the signaler
                _signaler = new NodeDssSignaler()
                {
                    /*HttpServerAddress = "http://10.44.160.22:3000/",
                    LocalPeerId = "DINF-D60015-43A",
                    RemotePeerId = "HOLOLENS-RO2J0",*/
                    HttpServerAddress = "http://" + Settings.m_sIPLocalUDP + ":3000/",
                    LocalPeerId = Settings.m_sLocalPeerId,// "DINF-D60015-43A",
                    RemotePeerId = Settings.m_sRemotePeerId, // "HOLOLENS-RO2J0",
            };
                _signaler.OnMessage += (NodeDssSignaler.Message msg) =>
                {
                    switch (msg.MessageType)
                    {
                        case NodeDssSignaler.Message.WireMessageType.Offer:
                            _peerConnection.SetRemoteDescription("offer", msg.Data);
                            _peerConnection.CreateAnswer();
                            break;

                        case NodeDssSignaler.Message.WireMessageType.Answer:
                            _peerConnection.SetRemoteDescription("answer", msg.Data);
                            break;

                        case NodeDssSignaler.Message.WireMessageType.Ice:
                            var parts = msg.Data.Split(new string[] { msg.IceDataSeparator },
                                StringSplitOptions.RemoveEmptyEntries);
                        // Note the inverted arguments for historical reasons.
                        // 'candidate' is last in AddIceCandidate(), but first in the message.
                        string sdpMid = parts[2];
                            int sdpMlineindex = int.Parse(parts[1]);
                            string candidate = parts[0];
                            _peerConnection.AddIceCandidate(sdpMid, sdpMlineindex, candidate);
                            break;
                    }
                };

                _signaler.StartPollingAsync();
            }

            // Interactions
            Debug.WriteLine("[Playback::start] Waiting for a connection...");

            m_dsUdpSocket = new DatagramSocket();
            m_dsUdpSocket.MessageReceived += Socket_MessageReceived;

            // Initialization UDP communication
            try
            {
                await m_dsUdpSocket.ConnectAsync(new EndpointPair(new HostName(Settings.m_sIPLocalUDP), Settings.m_sPortLocalUDP, new HostName(Settings.m_sIPRemoteUDP), Settings.m_sPortRemoteUDP));
                m_output = new DataWriter(m_dsUdpSocket.OutputStream);
                Debug.WriteLine("[Playback::start] UDP connection initialization ok");
            }
            catch (Exception)
            {
                Debug.WriteLine("[Playback::start] Error - UDP connection initialization ");
            }
        }


        //////////////////////////////////////////////////
        // For video streaming
        //////////////////////////////////////////////////

        private void App_Suspending(object sender, SuspendingEventArgs e)
        {
            if (_peerConnection != null)
            {
                _peerConnection.Close();
                _peerConnection.Dispose();
                _peerConnection = null;
            }
            //localVideoPlayerElement.SetMediaPlayer(null);
            if (_signaler != null)
            {
                _signaler.StopPollingAsync();
                _signaler = null;
            }
            remoteVideoPlayerElement.SetMediaPlayer(null);
        }

        private MediaStreamSource CreateI420VideoStreamSource(
    uint width, uint height, int framerate)
        {
            if (width == 0)
            {
                throw new ArgumentException("Invalid zero width for video.", "width");
            }
            if (height == 0)
            {
                throw new ArgumentException("Invalid zero height for video.", "height");
            }
            // Note: IYUV and I420 have same memory layout (though different FOURCC)
            // https://docs.microsoft.com/en-us/windows/desktop/medfound/video-subtype-guids
            var videoProperties = VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Iyuv, width, height);
            var videoStreamDesc = new VideoStreamDescriptor(videoProperties);
            videoStreamDesc.EncodingProperties.FrameRate.Numerator = (uint)framerate;
            videoStreamDesc.EncodingProperties.FrameRate.Denominator = 1;
            // Bitrate in bits per second : framerate * frame pixel size * I420=12bpp
            videoStreamDesc.EncodingProperties.Bitrate = ((uint)framerate * width * height * 12);
            var videoStreamSource = new MediaStreamSource(videoStreamDesc);
            videoStreamSource.BufferTime = TimeSpan.Zero;
            videoStreamSource.SampleRequested += OnMediaStreamSourceRequested;
            videoStreamSource.IsLive = true; // Enables optimizations for live sources
            videoStreamSource.CanSeek = false; // Cannot seek live WebRTC video stream
            return videoStreamSource;
        }

        private void OnMediaStreamSourceRequested(MediaStreamSource sender,
MediaStreamSourceSampleRequestedEventArgs args)
        {
            VideoBridge videoBridge;
            if (sender == _localVideoSource)
                videoBridge = _localVideoBridge;
            else if (sender == _remoteVideoSource)
                videoBridge = _remoteVideoBridge;
            else
                return;
            videoBridge.TryServeVideoFrame(args);
        }

       private void Peer_LocalI420AFrameReady(I420AVideoFrame frame)
        {

            lock(_localVideoLock)
            {
                if (!_localVideoPlaying)
                {
                    _localVideoPlaying = true;

                    // Capture the resolution into local variable useable from the lambda below
                    uint width = frame.width;
                    uint height = frame.height;

                    // Defer UI-related work to the main UI thread
                    RunOnMainThread(() =>
                    {
                        // Bridge the local video track with the local media player UI
                        int framerate = 30; // for lack of an actual value
                        _localVideoSource = CreateI420VideoStreamSource(
                            width, height, framerate);
                        var localVideoPlayer = new MediaPlayer();
                        localVideoPlayer.Source = MediaSource.CreateFromMediaStreamSource(
                            _localVideoSource);
                        //localVideoPlayerElement.SetMediaPlayer(localVideoPlayer);
                        remoteVideoPlayerElement.SetMediaPlayer(localVideoPlayer);
                        localVideoPlayer.Play();
                    });
                }
            }
            _localVideoBridge.HandleIncomingVideoFrame(frame);
        }

        private void Peer_RemoteI420AFrameReady(I420AVideoFrame frame)
        {
            lock (_remoteVideoLock)
            {
                if (!_remoteVideoPlaying)
                {
                    _remoteVideoPlaying = true;
                    uint width = frame.width;
                    uint height = frame.height;
                    RunOnMainThread(() =>
                    {
                        // Bridge the remote video track with the remote media player UI
                        int framerate = 30; // for lack of an actual value
                        _remoteVideoSource = CreateI420VideoStreamSource(width, height,
                            framerate);
                        var remoteVideoPlayer = new MediaPlayer();
                        remoteVideoPlayer.Source = MediaSource.CreateFromMediaStreamSource(
                            _remoteVideoSource);
                        remoteVideoPlayerElement.SetMediaPlayer(remoteVideoPlayer);
                        remoteVideoPlayer.Play();
                    });
                }
            }
            _remoteVideoBridge.HandleIncomingVideoFrame(frame);
        }

        private void RunOnMainThread(Windows.UI.Core.DispatchedHandler handler)
        {
            if (Dispatcher.HasThreadAccess)
            {
                handler.Invoke();
            }
            else
            {
                // Note: use a discard "_" to silence CS4014 warning
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, handler);
            }
        }

        private void Peer_LocalSdpReadytoSend(string type, string sdp)
        {
            var msg = new NodeDssSignaler.Message
            {
                MessageType = NodeDssSignaler.Message.WireMessageTypeFromString(type),
                Data = sdp,
                IceDataSeparator = "|"
            };
            _signaler.SendMessageAsync(msg);
        }

        private void Peer_IceCandidateReadytoSend(
            string candidate, int sdpMlineindex, string sdpMid)
        {
            var msg = new NodeDssSignaler.Message
            {
                MessageType = NodeDssSignaler.Message.WireMessageType.Ice,
                Data = $"{candidate}|{sdpMlineindex}|{sdpMid}",
                IceDataSeparator = "|"
            };
            _signaler.SendMessageAsync(msg);
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
            else if (m_iButtonArrowGuidanceStatus == 1 && Settings.m_guidanceType == Settings.ARROW_TYPE.ARROW_TYPE_CLICK)
            {
                Pointer p = e.Pointer;
                PointerPoint pPoint = e.GetCurrentPoint(this);

                Debug.WriteLine("Sending coordinates for arrow position");

                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);

                if (coordNorm[0] >= 0 && coordNorm[0] <= 1 && coordNorm[1] >= 0 && coordNorm[1] <= 1)
                {
                    sendUdpMessageNAVDIRECTION(coordNorm[0], coordNorm[1]);
                }
            }
        }

        private void Viewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (m_iButtonArrowGuidanceStatus == 1 && Settings.m_guidanceType == Settings.ARROW_TYPE.ARROW_TYPE_ZONES)
            {
                PointerPoint pPoint = e.GetCurrentPoint(this);
                double[] coordNorm = getNormalizedCoodinates(pPoint.Position.X, pPoint.Position.Y);

                if (coordNorm[0] < 0.35 && coordNorm[1] < 0.7 && coordNorm[1] > 0.3)
                {
                    sendUdpMessageNAVZONES(DIRECTIONS.DIRECTIONS_LEFT);
                }
                else if (coordNorm[0] > 0.65 && coordNorm[1] < 0.7 && coordNorm[1] > 0.3)
                {
                    sendUdpMessageNAVZONES(DIRECTIONS.DIRECTIONS_RIGHT);
                }
                else if (coordNorm[0] > 0.35 && coordNorm[0] < 0.65 && coordNorm[1] > 0.5)
                {
                    sendUdpMessageNAVZONES(DIRECTIONS.DIRECTIONS_UP);
                }
                else if (coordNorm[0] > 0.35 && coordNorm[0] < 0.65 && coordNorm[1] < 0.3)
                {
                    sendUdpMessageNAVZONES(DIRECTIONS.DIRECTIONS_DOWN);
                }
            }
        }

        private async void Viewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint pPoint = e.GetCurrentPoint(this);
            PointerPointProperties pPointProperties = pPoint.Properties;

            if (pPointProperties.MouseWheelDelta > 0)
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

            String annotationSelected = "";
            if (m_iButtonArrowGuidanceStatus == 1)
            {
                annotationSelected = "Arrow";
            }
            else if (m_iButtonArrowStatus == 1)
            {
                annotationSelected = "Target";
            }
            else if (m_iButtonNoEntryStatus == 1)
            {
                annotationSelected = "NoEntry";
            }

            await Task.Run(() =>
            {
                Task.Yield();
                Logger.m_logger.addLog("Changed annotation - Wheel - " + annotationSelected);
            });

        }

        //////////////////////////////////////////////////
        // GUI Controls
        //////////////////////////////////////////////////
        private void initializeGUI()
        {
            if (Settings.m_fullFeatures == false)
            {
                bnDebug.Opacity = 0;
                bnDebug.IsHitTestVisible = false;
            }
        }

        private async void bnArrow_Click(object sender, RoutedEventArgs e)
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

                await Task.Run(() =>
                {
                    Task.Yield();
                    Logger.m_logger.addLog("Changed annotation - Icon - Target");
                });
            }
        }

        private async void bnNoEntry_Click(object sender, RoutedEventArgs e)
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

                await Task.Run(() =>
                {
                    Task.Yield();
                    Logger.m_logger.addLog("Changed annotation - Icon - NoEntry");
                });
            }
        }

        private void changeButtonBackground(Button b, string imagePath)
        {
            string path = "ms-appx:///Assets/" + imagePath;

            ImageBrush ib = new ImageBrush();
            ib.ImageSource = new BitmapImage(new Uri(path));

            b.Background = ib;
        }

        private async void bnArrowGuidance_Click(object sender, RoutedEventArgs e)
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

                await Task.Run(() =>
                {
                    Task.Yield();
                    Logger.m_logger.addLog("Changed annotation - Icon - Arrow");
                });
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

        private void sendUdpMessageNAVZONES(DIRECTIONS d)
        {
            string msgToSend = "NAV ROTATE ";
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

        private void sendUdpMessageNAVDIRECTION(double x, double y)
        {
            string msgToSend = "NAV DIRECTION x=" + x + " y=" + y;
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
            
            try
            {
                m_output.WriteString(message);
                await m_output.StoreAsync();
                await Task.Run(() =>
                {
                    Task.Yield();
                    Logger.m_logger.addLog("Message sent - " + message);
                });
            }
            catch (Exception exception)
            {
                Debug.WriteLine("[Playback::sendUdpMessage] Send failed with error: " + exception.Message);
                Debug.WriteLine("[Playback::sendUdpMessage] Trying to reconnect");
                try
                {
                    await m_dsUdpSocket.ConnectAsync(new EndpointPair(new HostName(Settings.m_sIPLocalUDP), Settings.m_sPortLocalUDP, new HostName(Settings.m_sIPRemoteUDP), Settings.m_sPortRemoteUDP));
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

            await Task.Run(() =>
            {
                Task.Yield();
                Logger.m_logger.addLog("Message received - " + message);
            });

            if (message == "WARNING noHits")
            {
                await txNoHitsInfo.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Brush b = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    txNoHitsInfo.Foreground = b;

                });


                // That way is not really recommended by Microsoft, but ... it seems to work.
                await Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(task =>
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
        double[] getNormalizedCoodinates(double x, double y)
        {
            double[] toReturn = new double[2];

            toReturn[0] = x / 1280.0;
            toReturn[1] = Math.Abs(((y - 90) / (810 - 90)) - 1);

            double scalingFactorX = 5;
            double scalingFactorY = 5;
            double convergenceX = 0.65;
            double convergenceY = 0.35;
            toReturn[0] = toReturn[0] - (1 - convergenceX - (1 - toReturn[0])) / scalingFactorX;
            toReturn[1] = toReturn[1] - (1 - convergenceY - (1 - toReturn[1])) / scalingFactorY;

            Debug.WriteLine("[MainPage::getNormalizedCoodinates] Normalized coordinates: " + x + "=>" + toReturn[0] + " " + y + "=>" + toReturn[1]);

            return toReturn;

        }

        private void BnCreateOffer_Click(object sender, RoutedEventArgs e)
        {
            _peerConnection.CreateOffer();
        }
    }


}
