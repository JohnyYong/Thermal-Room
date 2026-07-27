// JoystickMapper.cs
// -----------------------------------------------------------------------------
// RUNTIME SCRIPT — uses the LEGACY Input Manager (UnityEngine.Input).
//
// Identifies which joystick BUTTON NUMBER and which AXIS NUMBER each physical
// control on your DualSense maps to. Mappings over Bluetooth differ from USB and
// between operating systems, so this lets you read YOUR exact layout.
//
// SETUP:
//   1. First run the editor menu: Tools > PS5 Mapper > Add 28 Discovery Axes
//      (this adds the Axis1..Axis28 definitions the legacy system needs).
//   2. Attach this script to any GameObject in your scene.
//   3. Pair the DualSense over Bluetooth, press Play.
//   4. Press one control at a time and watch the on-screen / Console readout.
//      Note the number next to each input — that's what you'll use:
//          Button:  Input.GetKey(KeyCode.JoystickButton<N>)
//          Axis:    Input.GetAxis("Axis<N>")
//
// TIP: Move ONE control at a time so it's obvious which number reacts.
// -----------------------------------------------------------------------------

using UnityEngine;
using System.Text;

public class JoystickMapper : MonoBehaviour
{
    [Header("Output")]
    public bool logToConsole = true;
    public bool showOnScreen = true;

    [Tooltip("Axis values with magnitude below this are ignored (resting drift).")]
    [Range(0f, 0.9f)]
    public float axisThreshold = 0.25f;

    [Tooltip("How many joystick buttons to scan (0..N-1). 20 covers the DualSense.")]
    public int buttonsToScan = 20;

    [Tooltip("How many axes to scan. Must match the axes added by the editor tool.")]
    public int axesToScan = 28;

    private readonly StringBuilder sb = new StringBuilder();

    void Update()
    {
        sb.Clear();

        // --- Connected joysticks ---
        string[] names = Input.GetJoystickNames();
        if (names.Length == 0 || string.IsNullOrEmpty(names[0]))
        {
            sb.AppendLine("No joystick detected.");
            sb.AppendLine("Pair the DualSense over Bluetooth, then press a button.");
        }
        else
        {
            sb.AppendLine("Connected joystick(s):");
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i]))
                    sb.AppendLine($"  [{i + 1}] {names[i]}");
        }
        sb.AppendLine("-----------------------------------");

        // --- Buttons 0..N ---
        sb.AppendLine("BUTTONS (press one):");
        for (int i = 0; i < buttonsToScan; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.JoystickButton0 + i);
            if (Input.GetKey(key))
            {
                sb.AppendLine($"  >> JoystickButton{i}  is PRESSED");
                if (logToConsole && Input.GetKeyDown(key))
                    Debug.Log($"Button pressed -> KeyCode.JoystickButton{i}");
            }
        }

        // --- Axes 1..N (requires the editor tool to have added them) ---
        sb.AppendLine("AXES (move one):");
        for (int i = 1; i <= axesToScan; i++)
        {
            string axisName = "Axis" + i;
            float value;
            try { value = Input.GetAxisRaw(axisName); }
            catch { continue; } // axis not defined; skip silently

            if (Mathf.Abs(value) >= axisThreshold)
            {
                sb.AppendLine($"  >> \"{axisName}\"  =  {value: 0.00;-0.00}");
                if (logToConsole)
                    Debug.Log($"Axis active -> \"{axisName}\" = {value:0.00}");
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreen) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.UpperLeft,
            wordWrap = false
        };
        style.normal.textColor = Color.white;
        GUI.Box(new Rect(8, 8, 480, 700), GUIContent.none);
        GUI.Label(new Rect(16, 14, 470, 690), sb.ToString(), style);
    }
}
