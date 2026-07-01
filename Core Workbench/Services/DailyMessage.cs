namespace Core_Workbench.Services
{
    /// <summary>
    /// Picks one message per calendar day. Deterministic — the same all day, and
    /// it advances to the next one each day, cycling through the whole list.
    /// </summary>
    public static class DailyMessage
    {
        private static readonly DateTime Epoch = new(2020, 1, 1);

        private static readonly string[] Messages =
        {
            "Another day, another boot. Let's make it count.",
            "Your PC believes in you. Mostly.",
            "Keep the temps low and the ambitions high.",
            "Small commits, big progress.",
            "Hydrate. Your CPU isn't the only thing that overheats.",
            "Today is a good day to defrag your goals.",
            "Ship it. You can refactor tomorrow.",
            "Close 12 of those 47 browser tabs. You can do it.",
            "Back up something today. Future you says thanks.",
            "Idle cycles are wasted cycles — but rest is allowed too.",
            "The best optimization is the one you actually finish.",
            "Stay cool. Literally and figuratively.",
            "Every expert was once a person staring at a stack trace.",
            "Your future self is watching. Make them proud.",
            "Run the update. Yes, that one you keep snoozing.",
            "Clean code, clean drives, clear mind.",
            "Touch grass, then touch keyboard.",
            "A reboot solves more than you'd like to admit.",
            "You are the admin of your own day.",
            "Less entropy, more energy.",
            "Make today's build a green one.",
            "Progress over perfection. Always.",
            "Even a 1% improvement compounds.",
            "Trust the process — and your backups.",
            "The cache of life rewards patience.",
            "Breathe in. Breathe out. Push to main.",
            "Your potential has no rate limit.",
            "Sharpen the axe before swinging.",
            "Done is better than perfect, but tested is better than done.",
            "Keep going. The loading bar always finishes.",
            "Be the signal, not the noise.",
            "One bug closed is one step forward.",
            "Stay curious; the docs are deeper than they look.",
            "Today's effort is tomorrow's muscle memory.",
            "Mind your storage and your storage will mind you.",
            "Greatness runs in the background.",
            "Reduce, reuse, refactor.",
            "Your machine is fast. Be faster at deciding.",
            "Logs don't lie. Read them.",
            "Make it work, make it right, make it fast.",
        };

        public static string Today()
        {
            int days = (int)(DateTime.Today - Epoch).TotalDays;
            int i = ((days % Messages.Length) + Messages.Length) % Messages.Length;
            return Messages[i];
        }
    }
}
