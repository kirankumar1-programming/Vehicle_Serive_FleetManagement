using System;
using System.Collections.Generic;

namespace VehicleService.Application.DTOs;

public class ChatRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? ContextUrl { get; set; }
}

public class ChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Intent { get; set; } = "General";
    public List<ChatSourceCitationDto> Sources { get; set; } = new();
    public List<ChatEntityCardDto> EntityCards { get; set; } = new();
    public List<ChatQuickActionDto> SuggestedActions { get; set; } = new();
    public List<string> SuggestedQuestions { get; set; } = new();
}

public class ChatSourceCitationDto
{
    public string Title { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string SourceType { get; set; } = "Document"; // "Document" | "Database"
    public double Score { get; set; }
    public string? LinkUrl { get; set; }
}

public class ChatEntityCardDto
{
    public string Type { get; set; } = string.Empty; // "Vehicle", "JobCard", "Invoice", "Estimate", "Part", "Appointment", "Warranty", "Roadside"
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = "primary"; // "success", "warning", "info", "danger", "primary"
    public Dictionary<string, string> KeyValues { get; set; } = new();
    public string? ActionUrl { get; set; }
    public string? ActionText { get; set; }
}

public class ChatQuickActionDto
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-arrow-right";
}

public class DocumentUploadDto
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = "text/plain";
}

public class DocumentSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

public class RagChunkMatchDto
{
    public int DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string ChunkText { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class RagTestResultDto
{
    public string Query { get; set; } = string.Empty;
    public string DetectedIntent { get; set; } = string.Empty;
    public List<RagChunkMatchDto> MatchedDocuments { get; set; } = new();
    public List<string> MatchedDatabaseRecords { get; set; } = new();
    public string SynthesizedAnswer { get; set; } = string.Empty;
}
