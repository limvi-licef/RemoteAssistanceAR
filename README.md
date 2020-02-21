# RemoteAssistanceAR

This project is developed in the context of the course INF6802 I followed at Polytechnique Montréal, as part of my PhD degree.

This PhD project aims at exploring how to assist people having cognitive deficiencies during their daily activities by means of augmented reality.
For this scenario, a person is looking for an object at home. She forgot where it is. She puts her augmented reality headset (Hololens 1 in our case), and calls a caregiver. The caregiver receives the audio and video stream from the Hololens and can add virtual annotations to the physical environment of the person. Those annotations are the following:
- Forbidden sign
- Target sign
- Sending a text message
- Showing a guidance arrow
The first 2 annotations are dependant of the depth. In other word, the caregiver clicks on the video stream, and the annotation is displayed on the first physical item found in the 3D environment of the person wearing the Hololens. This is done thanks to a raycast.
The last 2 annotations are independant of the depth. The text is shown for 10 seconds before being removed. The guidance arrow shows an arrow in front of the user, and following where caregiver clicks on the video stream, the direction of the arrow will change to show to the user where to go.

The solution is composed of 2 modules:
- The caregiver application, which is a UWP Windows desktop application.
- The user application, which is a Unity/UWP Hololens 1st generation application.
For audio / video stream, webrtc is used. A UDP channel is used to send some data to sync to 2 applications.

The following sections detail how to compile and run those applications. The final section explains how to use the system.

## RemoteApp
### Software requirements
Visual Studio 2017 with UWP support

### Compilation
Compile in Release x86.

### Run
The first time the application runs, you have to allow access to the microphone and the webcam, although the webcam won't be used (that was for testing purposes).

## HololensApp
### Software requirements
- Unity 2018.3.xx with UWP support.

### Compilation
- Open Assets > Scenes > SampleScene
- In File > Build Settings : select Universal Windows Platform in the left column.
- Select the following parameters in the right column:
	- Target device: Hololens
	- Architecture: x86
- Click Switch Platform.
- Click Build and Run.
- If necessary, create an output directory.


## How to use it?

(1) The communication between the 2 applications is done using Wifi. The first step is to make sure the Hololens and the Windows computer are in the same network. 
(2) In the 2 applications, the IP is hard-coded, which means you have to configure them manually to ensure they are valid in the network you are. To change them, proceed as follow:
- In the Windows app: In Settings.cs > m_sIPLocalUDP: set the IP of the Windows computer; m_sIPRemoteUDP: set the IP of the Hololens.
- In the Hololens app: Open UDPListener > SIP Local: set the IP of the Hololens; SIP Remote > set the IP of the Windows app.
(3) Start the Windows app.
(4) Start the Hololens app.
(5) In the Windows app, you have a "Call" button on the top right. Click on this button to initiate the connection.
(6) From there, the caregiver sees the video stream of the front webcam of the Hololens. The caregiver and the user can also communicate using audio.
(6) Features on the Windows app:
- The target and no-entry buttons are used to add the corresponding annotation at the location clicked on the video. The annotation is added to the first hit object in the environment.
- The arrow button displays an arrow in fron of the user. When the caregiver clicks on the video, the arrow points to the direction of the first hit object from the clicked location. Then, when the user moves, the arrows updates its direction to point to the clicked object.
- The "Send" button is used to send the text written on the text box next to the button. The text is then displayed on the top left corner of the Hololens.
- Optionally, some debug information can be displayed by clicking on the bug button. If the button is not displayed, first go to the Settings.cs file, and set the m_fullFeatures variable to True.
(7) Features on the Hololens app:
- There is one feature available at the moment: By locating the cursor to an annotation and by clicking on it, the user can remove it.
