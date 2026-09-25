using System;
using System.Collections.Generic;
using VehicleService.Domain.Common;

namespace VehicleService.Domain.Entities;

public class KnowledgeDocument : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // "Maintenance", "Warranty", "Roadside", "Fleet", "Pricing", "Manual"
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = "text/plain"; // ".txt", ".md", ".pdf"
    public string RawContent { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int ChunkCount { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

public class DocumentChunk : BaseEntity
{
    public int KnowledgeDocumentId { get; set; }
    public KnowledgeDocument? KnowledgeDocument { get; set; }

    public int ChunkIndex { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    public string ChunkText { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty; // Tokenized terms for fast lexical filtering
}

public class ChatMessageHistory : BaseEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string UserRole { get; set; } = "Guest";
    public string UserMessage { get; set; } = string.Empty;
    public string BotResponse { get; set; } = string.Empty;
    public string? MatchedSourcesJson { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
