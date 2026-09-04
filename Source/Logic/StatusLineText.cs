namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Toggle/Ground/Zone status label assembly behind
    /// InformationPanelHandler. Extracted (SRP — label text is not panel
    /// logic) so the one-liners run offline in StatusLineTextTests.
    /// Pure C#.
    /// </summary>
    public static class StatusLineText
    {
        public static string Toggle(bool isOn)
        {
            return "Toggle: " + (isOn ? "ON" : "OFF");
        }

        public static string Ground(bool active)
        {
            return "Ground: " + (active ? "Active" : "Dimmed");
        }

        public static string Zone(int index)
        {
            return "Zone: " + (index + 1);
        }
    }
}
