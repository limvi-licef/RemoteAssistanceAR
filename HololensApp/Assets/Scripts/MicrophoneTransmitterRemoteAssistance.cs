// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using HoloToolkit.Unity;
using HoloToolkit.Unity.InputModule;

#if !UNITY_EDITOR
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.Storage.Streams;
#endif

namespace HoloToolkit.Sharing.VoiceChat
{
    /// <summary>
    /// Transmits data from your microphone to other clients connected to a SessionServer. Requires any receiving client to be running the MicrophoneReceiver script.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MicrophoneTransmitterRemoteAssistance : MonoBehaviour
    {
        /// <summary>
        /// Which type of microphone/quality to access
        /// </summary>
        public MicStream.StreamCategory Streamtype = MicStream.StreamCategory.HIGH_QUALITY_VOICE;

        /// <summary>
        /// You can boost volume here as desired. 1 is default but probably too quiet. You can change during operation. 
        /// </summary>
        public float InputGain = 2;

        /// <summary>
        /// Whether or not to send the microphone data across the network
        /// </summary>
        public bool ShouldTransmitAudio = true;

        /// <summary>
        /// Whether other users should be able to hear the transmitted audio
        /// </summary>
        public bool Mute;

        public Transform GlobalAnchorTransform;

        public bool ShowInterPacketTime;

        private DateTime timeOfLastPacketSend;
        private float worstTimeBetweenPackets;

        private int sequenceNumber;

        private int sampleRateType = 3; // 48000Hz

        private AudioSource audioSource;

        private bool hasServerConnection;
        private bool micStarted;

        public const int AudioPacketSize = 960;
        private CircularBuffer m_micBuffer = new CircularBuffer(AudioPacketSize * 10 * 2 * 4, true);
        private byte[] m_packetSamples = new byte[AudioPacketSize * 4];

        // bit packers
        private readonly BitManipulator versionPacker = new BitManipulator(0x7, 0);           // 3 bits, 0 shift
        private readonly BitManipulator audioStreamCountPacker = new BitManipulator(0x38, 3); // 3 bits, 3 shift
        private readonly BitManipulator channelCountPacker = new BitManipulator(0x1c0, 6);    // 3 bits, 6 shift
        private readonly BitManipulator sampleRatePacker = new BitManipulator(0x600, 9);      // 2 bits, 9 shift
        private readonly BitManipulator sampleTypePacker = new BitManipulator(0x1800, 11);    // 2 bits, 11 shift
        private readonly BitManipulator sampleCountPacker = new BitManipulator(0x7fe000, 13); // 10 bits, 13 shift
        private readonly BitManipulator codecTypePacker = new BitManipulator(0x1800000, 23);  // 2 bits, 23 shift
        private readonly BitManipulator mutePacker = new BitManipulator(0x2000000, 25);  // 1 bits, 25 shift
        private readonly BitManipulator sequenceNumberPacker = new BitManipulator(0x7C000000, 26);  // 6 bits, 26 shift

        private readonly Mutex audioDataMutex = new Mutex();

        #region DebugVariables
        public bool HearSelf;

        private readonly CircularBuffer testCircularBuffer = new CircularBuffer(48000 * 2 * 4 * 3, true);
        private AudioSource testSource;
        public AudioClip TestClip;
        public bool SaveTestClip;
        #endregion

        public String m_sIPLocalUDP;
        public String m_sPortLocalUDP;
        public String m_sIPRemoteUDP;
        public String m_sPortRemoteUDP;
        /*public String m_sIPRemoteVideo;
        public String m_sPortRemoteVideo;*/
        private bool m_udpConnected;
        #if !UNITY_EDITOR
        private DatagramSocket m_dsUdpSocket;
        private DataWriter m_output;
        #endif

        /*private NetworkConnection GetActiveConnection()
        {
            NetworkConnection connection = null;
            var stage = SharingStage.Instance;
            if (stage && stage.Manager != null)
            {
                connection = stage.Manager.GetServerConnection();
            }
            if (connection == null || !connection.IsConnected())
            {
                Debug.LogFormat("[MicrophoneTransmitter::GetActiveConnection] No active connection");
                return null;
            }
            Debug.LogFormat("[MicrophoneTransmitter::GetActiveConnection] Active connection");
            return connection;
        }*/

        private async void Awake()
        {
            Debug.LogFormat("[MicrophoneTransmitter::Awake] Called\n");
            audioSource = GetComponent<AudioSource>();

            int errorCode = MicStream.MicInitializeCustomRate((int)Streamtype, AudioSettings.outputSampleRate);
            CheckForErrorOnCall(errorCode);
            if (errorCode == 0 || errorCode == (int)MicStream.ErrorCodes.ALREADY_RUNNING)
            {
                if (CheckForErrorOnCall(MicStream.MicSetGain(InputGain)))
                {
                    audioSource.volume = HearSelf ? 1.0f : 0.0f;
                    micStarted = CheckForErrorOnCall(MicStream.MicStartStream(false, false));
                }
            }

            /*m_sIPLocalUDP = "10.44.160.22";
            m_sPortLocalUDP = "62504";
            m_sIPRemoteUDP = "10.44.161.15";
            m_sPortRemoteUDP = "63777";*/
            Debug.LogFormat("[MicrophoneTransmitterRemoteAssistance::Awake] Trying to connect with the following data:\n- Local IP:"+ m_sIPLocalUDP +
                ":" + m_sPortLocalUDP + "\n- Remote IP: " + m_sIPRemoteUDP + ":" + m_sPortRemoteUDP + "\n");
#if !UNITY_EDITOR
                try
                {
                    m_dsUdpSocket = new DatagramSocket();
                    await m_dsUdpSocket.ConnectAsync(new EndpointPair(new HostName(m_sIPLocalUDP), m_sPortLocalUDP, new HostName(m_sIPRemoteUDP), m_sPortRemoteUDP));
                    m_output = new DataWriter(m_dsUdpSocket.OutputStream);
                    Debug.LogFormat("[MicrophoneTransmitterRemoteAssistance::Awake] UDP connection initialization - ok\n");
                    m_udpConnected = true;
                }
                catch (Exception e)
                {
                    Debug.LogFormat("[MicrophoneTransmitterRemoteAssistance::Awake] UDP connection initialization - error: \n" + e.ToString() + "\n" + SocketError.GetStatus(e.HResult).ToString() + "\n");
                    m_udpConnected = false;
                }
#endif
        }

        private void OnAudioFilterRead(float[] buffer, int numChannels)
        {
            //Debug.LogError("[MicrophoneTransmitterRemoteAssistance::OnAudioFilterRead] Called");
            try
            {
                audioDataMutex.WaitOne();

                if (micStarted && hasServerConnection)
                {
                    if (CheckForErrorOnCall(MicStream.MicGetFrame(buffer, buffer.Length, numChannels)))
                    {
                        int dataSize = buffer.Length * 4;
                        if (m_micBuffer.Write(buffer, 0, dataSize) != dataSize)
                        {
                            Debug.LogError("[MicrophoneTransmitterRemoteAssistance::OnAudioFilterRead] Send buffer filled up. Some audio will be lost.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MicrophoneTransmitterRemoteAssistance::OnAudioFilterRead] " + e.Message);
            }
            finally
            {
                audioDataMutex.ReleaseMutex();
            }
        }

        private void Update()
        {
            CheckForErrorOnCall(MicStream.MicSetGain(InputGain));
            audioSource.volume = HearSelf ? 1.0f : 0.0f;

            try
            {
                audioDataMutex.WaitOne();

                //var connection = GetActiveConnection();
                //hasServerConnection = (connection != null);
                hasServerConnection = m_udpConnected;
                if (hasServerConnection)
                {
                    Debug.LogError("[MicrophoneTransmitter::Update] Before transmitting the audio:\n- hasServerConnection: " + hasServerConnection + "\n" + "- micBuffer.UsedCapacity: " + m_micBuffer.UsedCapacity + "\n");
                    while (m_micBuffer.UsedCapacity >= 4 * AudioPacketSize)
                    {
                        //TransmitAudio(connection);
                        TransmitAudio(null);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MicrophoneTransmitter::Update] Error: " + e.Message);
            }
            finally
            {
                audioDataMutex.ReleaseMutex();
            }

#region DebugInfo
            if (SaveTestClip && testCircularBuffer.UsedCapacity == testCircularBuffer.TotalCapacity)
            {
                float[] testBuffer = new float[testCircularBuffer.UsedCapacity / 4];
                testCircularBuffer.Read(testBuffer, 0, testBuffer.Length * 4);
                testCircularBuffer.Reset();
                TestClip = AudioClip.Create("testclip", testBuffer.Length / 2, 2, 48000, false);
                TestClip.SetData(testBuffer, 0);
                if (!testSource)
                {
                    GameObject testObj = new GameObject("testclip");
                    testObj.transform.parent = transform;
                    testSource = testObj.AddComponent<AudioSource>();
                }
                testSource.PlayClip(TestClip);
                SaveTestClip = false;
            }
#endregion
        }

        private void TransmitAudio(NetworkConnection connection)
        {
            //Debug.LogFormat("[MicrophoneTransmitter::TransmitAudio] Called");
            m_packetSamples = new byte[AudioPacketSize * 4];
            m_micBuffer.Read(m_packetSamples, 0, 4 * AudioPacketSize);
            //SendFixedSizedChunk(connection, packetSamples, packetSamples.Length);
            sendUdpMessage(m_packetSamples);

            if (SaveTestClip)
            {
                testCircularBuffer.Write(m_packetSamples, 0, m_packetSamples.Length);
            }
        }

        private void SendFixedSizedChunk(NetworkConnection connection, byte[] data, int dataSize)
        {
            /*String temp = "";
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    //Debug.LogFormat(data[i] + "");
                    temp += data[i] + "";
                }

            }
            if ( temp != "")
            {
                Debug.LogFormat("[MicrophoneTransmitter::SendFixedSizedChunk] Called - Data are not empty!!! : " + temp + "\n");
            }*/

            DateTime currentTime = DateTime.Now;
            float seconds = (float)(currentTime - timeOfLastPacketSend).TotalSeconds;
            timeOfLastPacketSend = currentTime;
            if (seconds < 10.0)
            {
                if (worstTimeBetweenPackets < seconds)
                {
                    worstTimeBetweenPackets = seconds;
                }

                if (ShowInterPacketTime)
                {
                    Debug.LogFormat("Microphone: Milliseconds since last sent: {0}, Worst: {1}",
                        (seconds * 1000.0).ToString(CultureInfo.InvariantCulture),
                        (worstTimeBetweenPackets * 1000.0).ToString(CultureInfo.InvariantCulture));
                }
            }

            int clientId = SharingStage.Instance.Manager.GetLocalUser().GetID();

            // pack the header
            NetworkOutMessage msg = connection.CreateMessage((byte)MessageID.AudioSamples);

            int dataCountFloats = dataSize / 4;

            msg.Write((byte)5); // 8 byte header size

            Int32 pack = 0;
            versionPacker.SetBits(ref pack, 1);                   // version
            audioStreamCountPacker.SetBits(ref pack, 1);          // AudioStreamCount
            channelCountPacker.SetBits(ref pack, 1);              // ChannelCount
            sampleRatePacker.SetBits(ref pack, sampleRateType);   // SampleRate: 1 = 16000, 3 = 48000
            sampleTypePacker.SetBits(ref pack, 0);                // SampleType
            sampleCountPacker.SetBits(ref pack, dataCountFloats); // SampleCount (data count is in bytes and the actual data is in floats, so div by 4)
            codecTypePacker.SetBits(ref pack, 0);                 // CodecType
            mutePacker.SetBits(ref pack, Mute ? 1 : 0);
            sequenceNumberPacker.SetBits(ref pack, sequenceNumber++);
            sequenceNumber %= 32;

            msg.Write(pack); // the packed bits

            // This is where stream data starts. Write all data for one stream

            msg.Write(0.0f);     // average amplitude.  Not needed in direction from client to server.
            msg.Write(clientId); // non-zero client ID for this client.

            // HRTF position bits

            Vector3 cameraPosRelativeToGlobalAnchor = Vector3.zero;
            Vector3 cameraDirectionRelativeToGlobalAnchor = Vector3.zero;

            if (GlobalAnchorTransform != null)
            {
                cameraPosRelativeToGlobalAnchor = MathUtils.TransformPointFromTo(
                    null,
                    GlobalAnchorTransform,
                    CameraCache.Main.transform.position);

                cameraDirectionRelativeToGlobalAnchor = MathUtils.TransformDirectionFromTo(
                    null,
                    GlobalAnchorTransform,
                    CameraCache.Main.transform.position);
            }

            cameraPosRelativeToGlobalAnchor.Normalize();
            cameraDirectionRelativeToGlobalAnchor.Normalize();

            // Camera position
            msg.Write(cameraPosRelativeToGlobalAnchor.x);
            msg.Write(cameraPosRelativeToGlobalAnchor.y);
            msg.Write(cameraPosRelativeToGlobalAnchor.z);

            // HRTF direction bits
            msg.Write(cameraDirectionRelativeToGlobalAnchor.x);
            msg.Write(cameraDirectionRelativeToGlobalAnchor.y);
            msg.Write(cameraDirectionRelativeToGlobalAnchor.z);

            msg.WriteArray(data, (uint)dataCountFloats * 4);
            
            connection.Send(msg, MessagePriority.Immediate, MessageReliability.ReliableOrdered, MessageChannel.Audio, true);

            /*Debug.LogFormat("[MicrophoneTransmitter::SendFixedSizedChunk] msg: " + msg.ToString() + "\n");
            Debug.LogFormat("[MicrophoneTransmitter::SendFixedSizedChunk] data: ");
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                    Debug.LogFormat(data[i] + "");
            }
            Debug.LogFormat("\n");*/
            //Debug.LogFormat("[MicrophoneTransmitter::SendFixedSizedChunk] Message sent.");
        }

        public async void sendUdpMessage(byte[] message)
        {
#if !UNITY_EDITOR
        m_output.WriteBytes(message);
       
        try
        {
            await m_output.StoreAsync();
            //m_sStatus = "Message sent: " + message;
            String temp = "";
            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] != 0)
                {
                    //Debug.LogFormat(message[i] + "");
                    temp += message[i] + "";
                }

            }
            if ( temp != "")
            {
                Debug.LogFormat("[MicrophoneTransmitter::sendUdpMessage] Called - Data are not empty!!! : " + temp + "\n");
            }
        }
        catch (Exception exception)
        {
            Debug.LogFormat("[MicrophoneTransmitterRemoteAssistance::sendUdpMessage] Send failed with error: " + exception.Message);
        }
#endif
        }

        private void OnDestroy()
        {
            CheckForErrorOnCall(MicStream.MicDestroy());
        }

        private bool CheckForErrorOnCall(int returnCode)
        {
            return MicStream.CheckForErrorOnCall(returnCode);
        }

#if DOTNET_FX
        // on device, deal with all the ways that we could suspend our program in as few lines as possible
        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                CheckForErrorOnCall(MicStream.MicPause());
            }
            else
            {
                CheckForErrorOnCall(MicStream.MicResume());
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            OnApplicationPause(!focused);
        }

        private void OnDisable()
        {
            OnApplicationPause(true);
        }

        private void OnEnable()
        {
            OnApplicationPause(false);
        }
#endif
    }
}