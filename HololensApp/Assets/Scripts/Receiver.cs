// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using HoloToolkit.Unity.Receivers;
using HoloToolkit.Unity.InputModule;

public class Receiver : InteractionReceiver
{
    public UDPListener m_udp;

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
        if (obj.name == "bnCall")
        {
            Debug.Log("[Receiver::InputClicked] Button clicked to initiate call");
            m_udp.sendUdpMessage("CALL start");
        }
        
    }
}
