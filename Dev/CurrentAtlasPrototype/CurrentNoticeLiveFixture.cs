using System.Text;
using TMPro;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live test campaign. The interface is
    // implemented by a bar component whose concrete type is not guaranteed, so
    // the fixture searches loaded behaviours for INotificationBarUI and reads
    // the TMP text from its hierarchy.
    public static class CurrentNoticeLiveFixture
    {
        private const string Prefix = "[RadioNavigation][NOTICE-PROBE] ";

        // active|text, or missing:N when no implementing component is loaded.
        public static string Read()
        {
            Object[] loaded = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
            MonoBehaviour bar = null;
            for (int i = 0; i < loaded.Length; i++)
            {
                var candidate = loaded[i] as INotificationBarUI;
                if (candidate is MonoBehaviour behaviour && behaviour)
                {
                    bar = behaviour;
                    break;
                }
            }
            if (bar == null)
            {
                Debug.Log(Prefix + "missing:" + loaded.Length);
                return "missing:" + loaded.Length;
            }
            var text = new StringBuilder(256);
            text.Append(bar.gameObject.activeSelf ? "1" : "0").Append('|');
            TMP_Text[] labels = bar.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null || string.IsNullOrEmpty(labels[i].text))
                    continue;
                if (text.Length > 2)
                    text.Append(" / ");
                text.Append(labels[i].text);
            }
            string value = text.ToString();
            Debug.Log(Prefix + bar.GetType().Name + " " + value);
            return value;
        }
    }
}
