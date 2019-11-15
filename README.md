# RemoteAssistanceAR

This project is developed in the context of the course INF6802 I followed at Polytechnique Montréal, as part of my PhD degree.

This PhD project aims at exploring how to assist people having cognitive deficiencies during their daily activities by means of augmented reality.
For this scenario, a person is looking for an object at home. She forgot where it is. She puts her augmented reality headset (Hololens 1 in our case), and calls a caregiver. The caregiver receives the video stream from the Hololens and can add virtual annotations to the physical environment of the person. Those annotations are the following:
- Forbidden sign
- Target sign
- Sending a text message
- Showing a guidance arrow
The first 2 annotations are dependant of the depth. In other word, the caregiver clicks on the video stream, and the annotation is displayed on the first physical item found in the 3D environment of the person wearing the Hololens. This is done thanks to a raycast.
The 2 last annotations are independant of the depth. The text is shown for 10 seconds before being removed.

The video stream is grabbed thanks to the LowLatencyMRC module from Microsoft: https://github.com/Microsoft/MixedRealityCompanionKit/tree/master/MixedRemoteViewCompositor/Samples/LowLatencyMRC

This solution comes with 2 applications:
- A Windows app (based on the LowLatencyMRC module from Microsoft, but most of the code has been rewritten to add new functionalities), that compiles with Visual Studio 2015, in x86.
- A Unity app that is targeted to be deployed on the Hololens 1. It used several features of the HoloToolkit (https://github.com/Microsoft/MixedRealityToolkit-Unity/releases, version 2017.4.3). To use it, first install the dependencies for the HoloToolkit: https://github.com/microsoft/MixedRealityToolkit-Unity/blob/htk_release/GettingStarted.md, as well as Visual Studio 2017. The first time you open the project in Unity, only an empty scene is shown. The scene needs to be opened manually. It is located in Assets > Scenes > SampleScene. To compile, in File > Build Settings, select the "Universal Windows Platform" platform. Then select the following settings:
	- Target Device: Hololens
	- Architecture: x86
	- Build configuration: Release
- Click on Build.
- On the output directory, open the Visual Studio solution. Select "Release", "x86", "Device"; tjhen build and deploy the solution on the Hololens.
- 

How to use it?

(1) The communication between the 2 applications is done using Wifi. The first step is to make sure the Hololens and the Windows computer are in the same network. 
(2) In the 2 applications, the IP is hard-coded, which means you have to configure them manually to ensure they are valid in the network you are. To change them, proceed as follow:
- In the Windows app: In Playback.xaml.cs > m_sIPLocalUDP: set the IP of the Windows computer; m_sIPRemoteUDP: set the IP of the Hololens.
- In the Hololens app: Open UDPListener > SIP Local: set the IP of the Hololens; SIP Remote > set the IP of the Windows app.
(3) Start the Windows app.
(4) Start the Hololens app.
(5) In front of you, you will see a button. Click on it, and the video streaming should start, i.e. you should see the video stream on the Windows app. Note that you might need to allow the video streaming on the Hololens.
(6) Features on the Windows app:
...
(7) Features on the Hololens app:
...
