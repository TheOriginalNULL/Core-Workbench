namespace Core_Workbench.Models
{
    /// <summary>A single saved note.</summary>
    public sealed class Note
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Untitled";

        /// <summary>Legacy plain-text body (older notes / search preview).</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>Rich content as serialized FlowDocument XAML. Empty = use Body.</summary>
        public string DocumentXaml { get; set; } = string.Empty;

        public bool Pinned { get; set; }
        public DateTime Updated { get; set; } = DateTime.Now;

        /// <summary>Title shown in the list, falling back to a placeholder.</summary>
        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title.Trim();

        public override string ToString() => DisplayTitle;
    }
}
