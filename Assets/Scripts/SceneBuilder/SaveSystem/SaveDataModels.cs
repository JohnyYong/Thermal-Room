using System;
using System.Collections.Generic;
using UnityEngine;

//This file is mainly the data model

namespace SaveSystem
{
    [Serializable]
    public sealed class ComponentStateDto
    {
        public string type; //AssemblyQualifiedName
        public string json;
    }

    [Serializable]
    public sealed class EntityDto
    {
        public string prefabID; //Asset GUID
        public string instanceID;

        //Native JsonUtility 
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public List<ComponentStateDto> components = new();
    }

    //Add versioning system for saving states
    [Serializable]
    public sealed class ScenarioSaveDto
    {
        public const int CurrentVersion = 1; //Gets updated everytime a format gets changed

        public int version = CurrentVersion;
        public string savedAtUtc;
        public string sceneName; //Which scene does this belongs to

        public List<EntityDto> entities = new();
    }

    //public class SaveDataModels : MonoBehaviour
    //{

    //    // Start is called once before the first execution of Update after the MonoBehaviour is created
    //    void Start()
    //    {
        
    //    }

    //    // Update is called once per frame
    //    void Update()
    //    {
        
    //    }
    //}
}