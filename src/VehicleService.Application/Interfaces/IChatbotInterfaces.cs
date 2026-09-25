using System.Collections.Generic;
using System.Threading.Tasks;
using VehicleService.Application.DTOs;
using VehicleService.Domain.Entities;

namespace VehicleService.Application.Interfaces;

public class DatabaseRagResult
{
    public List<string> ContextSummaries { get; set; } = new();
    public List<ChatEntityCardDto> EntityCards { get; set; } = new();
    public List<ChatSourceCitationDto> DbCitations { get; set; } = new();
    public List<ChatQuickActionDto> Actions { get; set; } = new();
    public bool HasDirectDbMatches => ContextSummaries.Count > 0 || EntityCards.Count > 0;
}

public interface IDocumentKnowledgeService
{
    Task<List<RagChunkMatchDto>> SearchDocumentChunksAsync(string query, int topK = 4, string? category = null);
    Task<List<DocumentSummaryDto>> GetAllDocumentsAsync();
    Task<KnowledgeDocument?> GetDocumentByIdAsync(int id);
    Task<int> IngestDocumentAsync(DocumentUploadDto dto);
    Task<bool> DeleteDocumentAsync(int id);
    Task ReindexAllDocumentsAsync();
}

public interface IDatabaseRagService
{
    Task<DatabaseRagResult> QueryDatabaseContextAsync(string query, string userId, string userRole);
}

public interface IRagOrchestratorService
{
    Task<ChatResponseDto> ProcessQueryAsync(string message, string userId, string userRole, string? sessionId, string? contextUrl);
    Task<RagTestResultDto> TestRagPipelineAsync(string query, string userId, string userRole);
}

public interface IChatbotService
{
    Task<List<ChatQuickActionDto>> GetQuickPromptsForRoleAsync(string userRole);
    Task<List<ChatMessageHistory>> GetSessionHistoryAsync(string sessionId);
    Task ClearSessionHistoryAsync(string sessionId);
}
