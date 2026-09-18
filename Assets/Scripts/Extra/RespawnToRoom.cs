using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using System.Collections.Generic;

public class RespawnToRoom : MonoBehaviour
{
    [Header("Respawn")]
    [SerializeField] private string tagToDetect = "GameController";
    [SerializeField] private Transform respawnPos;
    [SerializeField] private bool matchRespawnRotation = true;

    [Header("Cheats")]
    [SerializeField] private KeyCode refreshSceneCheatCode = KeyCode.R;

    private InputDevice rightHand;
    private bool wasPressed;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(tagToDetect)) return;
        Respawn(other.transform);
    }

    private void Respawn(Transform rig)
    {
        var cc = rig.GetComponent<CharacterController>();
        var cam = Camera.main != null ? Camera.main.transform : null;

        if (cc != null) cc.enabled = false;

        if (matchRespawnRotation && cam != null)
        {
            float delta = respawnPos.eulerAngles.y - cam.eulerAngles.y;
            rig.Rotate(0f, delta, 0f);
        }

        Vector3 offset = Vector3.zero;
        if (cam != null)
        {
            offset = cam.position - rig.position;
            offset.y = 0f;
        }

        rig.position = respawnPos.position - offset;

        if (cc != null) cc.enabled = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(refreshSceneCheatCode))
        {
            RefreshScene();
            return;
        }

        if (!rightHand.isValid)
        {
            TryGetRightHand();
            if (!rightHand.isValid) return;
        }

        if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed))
        {
            if (pressed && !wasPressed) RefreshScene();
            wasPressed = pressed;
        }
    }

    private void TryGetRightHand()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices);

        if (devices.Count > 0) rightHand = devices[0];
    }

    private void RefreshScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}