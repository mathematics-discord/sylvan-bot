// ReSharper disable ClassNeverInstantiated.Global

using static Discord.TextInputStyle;

namespace SylvanBot.Features.Announcements;

public sealed class AnnouncementModal : IModal
{
    public string Title => "Send announcement";

    [InputLabel("Title")] [ModalTextInput("title", minLength: 1, maxLength: 100)] public string AnnouncementTitle { get; set; } = string.Empty;

    [InputLabel("Body")] [ModalTextInput("body", style: Paragraph, minLength: 1, maxLength: 3850)] public string Body { get; set; } = string.Empty;
}