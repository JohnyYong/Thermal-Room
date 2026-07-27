// InputAxisAutoSetup.cs
// -----------------------------------------------------------------------------
// EDITOR SCRIPT — must live in a folder named "Editor"
//   e.g. Assets/Editor/InputAxisAutoSetup.cs
//
// The legacy Input Manager cannot read a joystick axis unless that axis has
// been defined in Project Settings > Input Manager. Manually adding 28 axes is
// tedious, so this tool does it for you.
//
// USAGE:
//   After the script compiles, click the menu:
//        Tools > PS5 Mapper > Add 28 Discovery Axes
//   This adds entries "Axis1" .. "Axis28", each bound to joystick axis 1..28,
//   for ALL joysticks. Your JoystickMapper runtime script can then read them.
//
//   To clean up afterwards:
//        Tools > PS5 Mapper > Remove Discovery Axes
// -----------------------------------------------------------------------------

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class InputAxisAutoSetup
{
    private enum AxisType { KeyOrMouseButton = 0, MouseMovement = 1, JoystickAxis = 2 }

    private class InputAxis
    {
        public string name;
        public float dead = 0.001f;
        public float sensitivity = 1f;
        public AxisType type = AxisType.JoystickAxis;
        public int axis;     // 0-based: 0 => joystick "1st axis"
        public int joyNum;   // 0 => all joysticks
    }

    [MenuItem("Tools/PS5 Mapper/Add 28 Discovery Axes")]
    public static void AddDiscoveryAxes()
    {
        for (int i = 0; i < 28; i++)
        {
            AddAxis(new InputAxis
            {
                name = "Axis" + (i + 1),
                axis = i,        // 0-based index -> joystick axis (i+1)
                joyNum = 0,      // read from any connected joystick
                dead = 0.001f,
                sensitivity = 1f,
                type = AxisType.JoystickAxis
            });
        }
        Debug.Log("PS5 Mapper: Added Axis1..Axis28 to the Input Manager.");
    }

    [MenuItem("Tools/PS5 Mapper/Remove Discovery Axes")]
    public static void RemoveDiscoveryAxes()
    {
        SerializedObject so = GetInputManager();
        SerializedProperty axes = so.FindProperty("m_Axes");

        for (int i = axes.arraySize - 1; i >= 0; i--)
        {
            string n = axes.GetArrayElementAtIndex(i)
                           .FindPropertyRelative("m_Name").stringValue;
            if (n != null && n.StartsWith("Axis") && int.TryParse(n.Substring(4), out _))
                axes.DeleteArrayElementAtIndex(i);
        }
        so.ApplyModifiedProperties();
        Debug.Log("PS5 Mapper: Removed discovery axes (Axis1..Axis28).");
    }

    // --- internals -------------------------------------------------------------

    private static SerializedObject GetInputManager()
    {
        return new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
    }

    private static bool AxisDefined(string axisName)
    {
        SerializedObject so = GetInputManager();
        SerializedProperty axes = so.FindProperty("m_Axes");
        for (int i = 0; i < axes.arraySize; i++)
        {
            if (axes.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("m_Name").stringValue == axisName)
                return true;
        }
        return false;
    }

    private static void AddAxis(InputAxis axis)
    {
        if (AxisDefined(axis.name)) return;

        SerializedObject so = GetInputManager();
        SerializedProperty axes = so.FindProperty("m_Axes");
        axes.arraySize++;
        so.ApplyModifiedProperties();

        SerializedProperty entry = axes.GetArrayElementAtIndex(axes.arraySize - 1);
        entry.FindPropertyRelative("m_Name").stringValue = axis.name;
        entry.FindPropertyRelative("descriptiveName").stringValue = "";
        entry.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
        entry.FindPropertyRelative("negativeButton").stringValue = "";
        entry.FindPropertyRelative("positiveButton").stringValue = "";
        entry.FindPropertyRelative("altNegativeButton").stringValue = "";
        entry.FindPropertyRelative("altPositiveButton").stringValue = "";
        entry.FindPropertyRelative("gravity").floatValue = 0f;
        entry.FindPropertyRelative("dead").floatValue = axis.dead;
        entry.FindPropertyRelative("sensitivity").floatValue = axis.sensitivity;
        entry.FindPropertyRelative("snap").boolValue = false;
        entry.FindPropertyRelative("invert").boolValue = false;
        entry.FindPropertyRelative("type").intValue = (int)axis.type;
        entry.FindPropertyRelative("axis").intValue = axis.axis;
        entry.FindPropertyRelative("joyNum").intValue = axis.joyNum;

        so.ApplyModifiedProperties();
    }
}
#endif
