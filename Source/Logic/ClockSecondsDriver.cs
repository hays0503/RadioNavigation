using System;
using System.Globalization;
using System.Reflection;
using DWS.Common.InjectionFramework;
using TMPro;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Core.Data;
using UBOAT.Game.UI;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Applies seconds to the vanilla HUD and journal clocks after the game's
    /// Update writers have run. It reads only campaign time and repairs a label
    /// if the game overwrites the seconds without advancing that time.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class ClockSecondsDriver : MonoBehaviour
    {
        private const string RootName = "RadioNavigation.ClockSecondsDriver";
        private const string DateFormatKey = "Date Format 1";

        private GameTime gameTime;
        private Locale locale;
        private Component contactsComponent;
        private Component journalComponent;
        private TextMeshProUGUI contactsDateLabel;
        private TextMeshProUGUI gmtLabel;
        private TextMeshProUGUI localLabel;
        private TextMeshProUGUI germanLabel;
        private FieldInfo contactsDateField;
        private FieldInfo gmtField;
        private FieldInfo localField;
        private FieldInfo germanField;

        private string sourceDateFormat;
        private string secondsDateFormat;
        private string expectedContactsText;
        private string expectedGmtText;
        private string expectedLocalText;
        private string expectedGermanText;
        private string cultureName;
        private long contactsSecond = long.MinValue;
        private long gmtSecond = long.MinValue;
        private long localSecond = long.MinValue;
        private long germanSecond = long.MinValue;
        private bool unsupportedDateFormatLogged;
        private bool failureLogged;

        /// <summary>
        /// Creates one persistent owner. The name-based check deliberately
        /// tolerates the game's mod assembly being loaded more than once.
        /// </summary>
        public static void EnsureRunning()
        {
            GameObject root = GameObject.Find(RootName);
            if (root != null)
            {
                MonoBehaviour[] existing = root.GetComponents<MonoBehaviour>();
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i].IsNotNull() && existing[i].GetType().Name == nameof(ClockSecondsDriver))
                        return;
                }
            }
            else
            {
                root = new GameObject(RootName);
                DontDestroyOnLoad(root);
            }

            root.AddComponent<ClockSecondsDriver>();
        }

        public void LateUpdate()
        {
            try
            {
                InjectionFramework framework = InjectionFramework.Instance;
                if (framework == null)
                    return;

                ResolveGameServices(framework);
                if (gameTime == null)
                    return;

                string currentCultureName = CultureInfo.CurrentCulture.Name;
                if (!string.Equals(cultureName, currentCultureName, StringComparison.Ordinal))
                {
                    cultureName = currentCultureName;
                    contactsSecond = gmtSecond = localSecond = germanSecond = long.MinValue;
                    expectedContactsText = expectedGmtText = expectedLocalText = expectedGermanText = null;
                }

                UpdateContactsClock();
                UpdateJournalClocks(framework);
            }
            catch (Exception exception)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    Debug.LogWarning("[RadioNav][Clock] Clock update failed: " + exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private void ResolveGameServices(InjectionFramework framework)
        {
            gameTime = framework.GetInstance<GameTime>();
            locale = framework.GetInstance<Locale>();

            IContactsUI contacts = framework.GetInstance<IContactsUI>();
            Component nextContacts = contacts as Component;
            if (nextContacts != contactsComponent)
            {
                contactsComponent = nextContacts;
                contactsDateLabel = null;
                contactsDateField = null;
                expectedContactsText = null;
                contactsSecond = long.MinValue;
            }

            IJournalUI journal = framework.GetInstance<IJournalUI>();
            Component nextJournal = journal as Component;
            if (nextJournal != journalComponent)
            {
                journalComponent = nextJournal;
                gmtLabel = null;
                localLabel = null;
                germanLabel = null;
                gmtField = null;
                localField = null;
                germanField = null;
                expectedGmtText = null;
                expectedLocalText = null;
                expectedGermanText = null;
                gmtSecond = localSecond = germanSecond = long.MinValue;
            }
        }

        private void UpdateContactsClock()
        {
            if (contactsComponent == null || locale == null)
                return;

            if (contactsDateField == null)
                contactsDateField = TypeMemberCache.GetInstanceMember(contactsComponent.GetType(), "dateLabel") as FieldInfo;
            if (contactsDateLabel == null && contactsDateField != null)
                contactsDateLabel = contactsDateField.GetValue(contactsComponent) as TextMeshProUGUI;
            if (contactsDateLabel == null)
                return;

            string currentFormat = locale[DateFormatKey];
            if (!string.Equals(sourceDateFormat, currentFormat, StringComparison.Ordinal))
            {
                sourceDateFormat = currentFormat;
                if (!ClockSecondsText.TryAddSecondsToDateFormat(sourceDateFormat, out secondsDateFormat))
                {
                    if (!unsupportedDateFormatLogged)
                    {
                        unsupportedDateFormatLogged = true;
                        Debug.LogWarning("[RadioNav][Clock] Unsupported localized Date Format 1; HUD clock left unchanged.");
                    }
                    return;
                }
                expectedContactsText = null;
                contactsSecond = long.MinValue;
            }

            DateTime localTime = gameTime.LocalDateTime;
            long key = ClockSecondsText.SecondKey(localTime);
            if (key != contactsSecond || expectedContactsText == null)
            {
                contactsSecond = key;
                expectedContactsText = ClockSecondsText.FormatLocalizedDate(secondsDateFormat, localTime);
            }

            // The native timed callback writes at minute granularity. Compare
            // the actual label even when the time key is unchanged, so that a
            // native rewrite cannot leave the seconds missing until next tick.
            if (!string.Equals(contactsDateLabel.text, expectedContactsText, StringComparison.Ordinal))
                contactsDateLabel.SetText(expectedContactsText);
        }

        private void UpdateJournalClocks(InjectionFramework framework)
        {
            if (journalComponent.IsNull() || !journalComponent.gameObject.activeInHierarchy)
                return;

            if (gmtField == null)
                gmtField = TypeMemberCache.GetInstanceMember(journalComponent.GetType(), "gmtTimeLabel") as FieldInfo;
            if (localField == null)
                localField = TypeMemberCache.GetInstanceMember(journalComponent.GetType(), "localTimeLabel") as FieldInfo;
            if (germanField == null)
                germanField = TypeMemberCache.GetInstanceMember(journalComponent.GetType(), "germanTimeLabel") as FieldInfo;
            if (gmtLabel == null && gmtField != null)
                gmtLabel = gmtField.GetValue(journalComponent) as TextMeshProUGUI;
            if (localLabel == null && localField != null)
                localLabel = localField.GetValue(journalComponent) as TextMeshProUGUI;
            if (germanLabel == null && germanField != null)
                germanLabel = germanField.GetValue(journalComponent) as TextMeshProUGUI;

            UpdateJournalLabel(gmtLabel, gameTime.GmtDateTime, ref gmtSecond, ref expectedGmtText);

            TimeZoneTracker tracker = framework.GetInstance<TimeZoneTracker>();
            if (tracker != null)
                UpdateJournalLabel(localLabel, gameTime.LocalDateTime, ref localSecond, ref expectedLocalText);

            UpdateJournalLabel(germanLabel, gameTime.CurrentDateTime, ref germanSecond, ref expectedGermanText);
        }

        private static void UpdateJournalLabel(
            TextMeshProUGUI label, DateTime time, ref long cachedSecond, ref string expectedText)
        {
            if (label == null)
                return;

            long key = ClockSecondsText.SecondKey(time);
            if (key != cachedSecond || expectedText == null)
            {
                cachedSecond = key;
                expectedText = ClockSecondsText.FormatClock(time);
            }
            if (!string.Equals(label.text, expectedText, StringComparison.Ordinal))
                label.SetText(expectedText);
        }

        public void OnDestroy()
        {
            try
            {
                if (gameTime == null)
                    return;

                RestoreIfOwned(contactsDateLabel, expectedContactsText,
                    sourceDateFormat == null ? null : FormatNativeDate(sourceDateFormat, gameTime.LocalDateTime));
                RestoreIfOwned(gmtLabel, expectedGmtText, ClockSecondsText.FormatClockWithoutSeconds(gameTime.GmtDateTime));
                RestoreIfOwned(localLabel, expectedLocalText, ClockSecondsText.FormatClockWithoutSeconds(gameTime.LocalDateTime));
                RestoreIfOwned(germanLabel, expectedGermanText, ClockSecondsText.FormatClockWithoutSeconds(gameTime.CurrentDateTime));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RadioNav][Clock] Could not restore native clock text: " + exception.GetType().Name);
            }
        }

        private static string FormatNativeDate(string format, DateTime value)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format,
                value, value.Day, value.Year, value.Hour, value.Minute);
        }

        private static void RestoreIfOwned(TextMeshProUGUI label, string ownedText, string nativeText)
        {
            if (label != null && ownedText != null && nativeText != null
                && string.Equals(label.text, ownedText, StringComparison.Ordinal))
                label.SetText(nativeText);
        }
    }
}
