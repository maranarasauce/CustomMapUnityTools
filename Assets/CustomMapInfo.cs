using System;
using UnityEngine;

public class CustomMapInfo : MonoBehaviour
{
    public MapInfo info;

    [Serializable]
    public class MapInfo
    {
        [NonSerialized] public int protocol = 1;
        public string mapName;
        public int versionNumber = 1;
        public Color nameColor = Color.white;
        public GadgetConfig gadgetConfig;
    }

    public enum GadgetConfig : int
    {
        PlayerPref = 0,
        RemoveGadgets = 1,
        KeepGadgets = 2
    }
}