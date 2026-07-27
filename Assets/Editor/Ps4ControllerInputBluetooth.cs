using UnityEngine;

// joystick button 0 = Button Square     = West 
// joystick button 1    = Button Cross      = South
// joystick button 2    = Button Circle     = East
// joystick button 3    = Button Triangle   = North
// Joystick button 4    = Button L1         = Left Shoulder
// Joystick button 5    = Button R1         = Right Shoulder
// Joystick button 6    = Button L2         = Left Trigger as Button
// Joystick button 7    = Button R2         = Right Trigger as Button       
// Joystick button 8    = Share Button
// Joystick button 9    = Options Button
// Joystick button 10   = Button L3         = Left Thumbstick Button
// Joystick button 11   = Button R3         = Right Thumbstick Button
// Joystick button 12   = Button PS         = Playstation Button
// Joystick button 13   = Button Touchpad
 
// Thumbsticks
// X axis   = Left Thumbstick "Horizontal"
// 3d axis  = Left Thumbstick "Vertical"
// 4th axis = Right Thumbstick "Horizontal"
// 7th axis = Right Thumbstick "Vertical"
 
// Triggers as Axis
// 5th axis = Trigger L2 = Left Trigger as Axis  = Range -1.0 to 1.0
// 6th axis = Trigger R2 = Right Trigger as Axis = Range -1.0 to 1.0
 
// D-Pad as Axis
// 8th Axis = D-Pad Horizontal  = Directional pad buttons Left and Right as Axis. Left = -1 and right = 1
// 9th axis = D-Pad Vertical    = Directional pad buttons Up and Down as Axis. Up = 1 and Down = -1
 
using System.Collections;
using System.Collections.Generic;
 
public class PS4ControllerInputMapBluetooth : MonoBehaviour
{
    void Update()
    {
        if (Input.GetButtonDown("Button Square"))
            Debug.Log("Button Square");
        if (Input.GetButtonDown("Button Cross"))
            Debug.Log("Button Cross");
        if (Input.GetButtonDown("Button Circle"))
            Debug.Log("Button Circle");
        if (Input.GetButtonDown("Button Triangle"))
            Debug.Log("Button Triangle");

        if (Input.GetButtonDown("Button L1"))
            Debug.Log("Button L1");
        if (Input.GetButtonDown("Button R1"))
            Debug.Log("Button R1");
        if (Input.GetButtonDown("Button L2"))
            Debug.Log("Button L2");
        if (Input.GetButtonDown("Button R2"))
            Debug.Log("Button R2");

        if (Input.GetButtonDown("Button Share"))
            Debug.Log("Button Share");
        if (Input.GetButtonDown("Button Options"))
            Debug.Log("Button Options");

        if (Input.GetButtonDown("Button L3"))
            Debug.Log("Button L3");
        if (Input.GetButtonDown("Button R3"))
            Debug.Log("Button R3");

        if (Input.GetButtonDown("Button PS"))
            Debug.Log("Button PS");
        if (Input.GetButtonDown("Button Touchpad"))
            Debug.Log("Button Touchpad");

        if (Input.GetAxis("Thumbstick Left Horizontal") != 0)
            Debug.Log("Thumbstick Left Horizontal = " + Input.GetAxis("Thumbstick Left Horizontal"));
        if (Input.GetAxis("Thumbstick Left Vertical") != 0)
            Debug.Log("Thumbstick Left Vertical = " + Input.GetAxis("Thumbstick Left Vertical"));
        if (Input.GetAxis("Thumbstick Right Horizontal") != 0)
            Debug.Log("Thumbstick Right Horizontal = " + Input.GetAxis("Thumbstick Right Horizontal"));
        if (Input.GetAxis("Thumbstick Right Vertical") != 0)
            Debug.Log("Thumbstick Right Vertical = " + Input.GetAxis("Thumbstick Right Vertical"));

        if (Input.GetAxis("Trigger L2") != -1)
            Debug.Log("Trigger L2 = " + Input.GetAxis("Trigger L2"));
        if (Input.GetAxis("Trigger R2") != -1)
            Debug.Log("Trigger R2: " + Input.GetAxis("Trigger R2"));
        if (Input.GetAxis("D-Pad Horizontal") != 0)
            Debug.Log("D-Pad Horizontal: " + Input.GetAxis("D-Pad Horizontal"));
        if (Input.GetAxis("D-Pad Vertical") != 0)
            Debug.Log("D-Pad Vertical: " + Input.GetAxis("D-Pad Vertical"));
    }
}