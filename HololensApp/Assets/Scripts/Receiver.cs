// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using HoloToolkit.Unity.Receivers;
using HoloToolkit.Unity.InputModule;
using HoloToolkit.Unity.SpatialMapping;

public class Receiver : InteractionReceiver
{
    public UDPListener m_udp;
    public SpatialMappingManager m_spatialMappingManager;
    public AnnotationsManager m_annotationManager;

    void Start()
     {

     }

    protected override void FocusEnter(GameObject obj, PointerSpecificEventData eventData)
    {

    }

    protected override void FocusExit(GameObject obj, PointerSpecificEventData eventData)
    {

    }

    protected override void InputDown(GameObject obj, InputEventData eventData)
    {

    }

    protected override void InputUp(GameObject obj, InputEventData eventData)
    {

    }

    protected override void InputClicked(GameObject obj, InputClickedEventData eventData)
    {
        Debug.Log(obj.name);
        if (obj.name == "bnCall")
        {
            Debug.Log("[Receiver::InputClicked] Button clicked to initiate call");
            m_udp.sendUdpMessage("CALL start");
        }
        else if(obj.name == "bnDisplayMesh")
        {
            //m_spatialMappingManager.DrawVisualMeshes = !m_spatialMappingManager.DrawVisualMeshes;
            //ButtonController b = (ButtonController)obj;
            /*if (m_annotationManager.getProcessDebug() == 0)
                m_annotationManager.processDebug(2);
            else if (m_annotationManager.getProcessDebug() == 2)
                m_annotationManager.processDebug(0);

            m_annotationManager.setDebugText("Test display debug message");*/
            m_annotationManager.enableGuidanceArrow(true);
        }
    }
}
