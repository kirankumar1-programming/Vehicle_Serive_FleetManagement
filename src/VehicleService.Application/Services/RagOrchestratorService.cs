using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;

namespace VehicleService.Application.Services;

public class RagOrchestratorService : IRagOrchestratorService, IChatbotService
{
    private readonly IDocumentKnowledgeService _docService;
    private readonly IDatabaseRagService _dbRagService;
    private readonly IUnitOfWork _unitOfWork;

    public RagOrchestratorService(
        IDocumentKnowledgeService docService,
        IDatabaseRagService dbRagService,
        IUnitOfWork unitOfWork)
    {
        _docService = docService;
        _dbRagService = dbRagService;
        _unitOfWork = unitOfWork;
    }

    public async Task<ChatResponseDto> ProcessQueryAsync(string message, string userId, string userRole, string? sessionId, string? contextUrl)
    {
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
        string cleanMsg = (message ?? "").Trim();

        var response = new ChatResponseDto
        {
            SessionId = sessionId
        };

        if (string.IsNullOrWhiteSpace(cleanMsg))
        {
            response.Reply = "Hello! I am **AutoBot**, your AutoPro Fleet & Workshop AI Assistant. How can I help you today?";
            response.SuggestedQuestions = GetDefaultFollowups(userRole);
            return response;
        }

        string lower = cleanMsg.ToLower();

        // 1. GREETING CHECK
        if (IsGreeting(lower))
        {
            response.Intent = "Greeting";
            response.Reply = $"👋 Hello! I am **AutoBot**, your AI assistant for AutoPro Fleet & Service Hub. I am grounded with our **official service manuals, warranty policies, 24/7 roadside protocols**, and **live database records** for your vehicles, appointments, and repair orders.\n\nHow can I assist you right now?";
            response.SuggestedActions = await GetQuickPromptsForRoleAsync(userRole);
            response.SuggestedQuestions = GetDefaultFollowups(userRole);
            await SaveHistoryAsync(sessionId, userId, userRole, cleanMsg, response.Reply, response.Sources);
            return response;
        }

        // 2. PARALLEL RETRIEVAL (Documents + Live Database)
        var docChunksTask = _docService.SearchDocumentChunksAsync(cleanMsg, topK: 3);
        var dbRagTask = _dbRagService.QueryDatabaseContextAsync(cleanMsg, userId, userRole);

        await Task.WhenAll(docChunksTask, dbRagTask);

        var docChunks = await docChunksTask;
        var dbRag = await dbRagTask;

        // 3. DETERMINE INTENT & SUPPRESS UNRELATED DOC CHUNKS FOR PERSONAL RECORD QUERIES
        bool isPureAccountOrDbQuery = IsPureAccountOrDbQuery(lower);
        if (isPureAccountOrDbQuery)
        {
            docChunks.Clear();
        }

        bool hasDocMatches = docChunks.Count > 0 && docChunks[0].Score > 0.4;
        bool hasDbMatches = dbRag.HasDirectDbMatches;

        if (hasDocMatches && hasDbMatches)
        {
            response.Intent = "HybridQuery";
        }
        else if (hasDbMatches)
        {
            response.Intent = "DatabaseQuery";
        }
        else if (hasDocMatches)
        {
            response.Intent = "DocumentQuery";
        }
        else
        {
            response.Intent = "GeneralInquiry";
        }

        // Attach citations & entity cards
        response.EntityCards = dbRag.EntityCards;
        response.SuggestedActions = dbRag.Actions;

        foreach (var chunk in docChunks.Where(c => c.Score > 0.4))
        {
            response.Sources.Add(new ChatSourceCitationDto
            {
                Title = chunk.DocumentTitle,
                Section = chunk.SectionTitle,
                Snippet = TruncateSnippet(chunk.ChunkText, 140),
                SourceType = "Document",
                Score = chunk.Score
            });
        }

        foreach (var dbCit in dbRag.DbCitations)
        {
            response.Sources.Add(dbCit);
        }

        // 4. SYNTHESIZE ANSWER
        response.Reply = SynthesizeResponse(cleanMsg, response.Intent, userRole, docChunks, dbRag);

        // 5. ATTACH RELEVANT FOLLOW-UPS & ACTIONS
        PopulateDynamicActionsAndFollowups(response, userRole, lower);

        // 6. PERSIST TURN
        await SaveHistoryAsync(sessionId, userId, userRole, cleanMsg, response.Reply, response.Sources);

        return response;
    }

    public async Task<RagTestResultDto> TestRagPipelineAsync(string query, string userId, string userRole)
    {
        var docChunks = await _docService.SearchDocumentChunksAsync(query, topK: 5);
        var dbRag = await _dbRagService.QueryDatabaseContextAsync(query, userId, userRole);

        string intent = (docChunks.Count > 0 && dbRag.HasDirectDbMatches) ? "Hybrid" :
                        dbRag.HasDirectDbMatches ? "Database Live" :
                        docChunks.Count > 0 ? "Document Knowledge" : "General";

        string synthesized = SynthesizeResponse(query, intent, userRole, docChunks, dbRag);

        return new RagTestResultDto
        {
            Query = query,
            DetectedIntent = intent,
            MatchedDocuments = docChunks,
            MatchedDatabaseRecords = dbRag.ContextSummaries,
            SynthesizedAnswer = synthesized
        };
    }

    public Task<List<ChatQuickActionDto>> GetQuickPromptsForRoleAsync(string userRole)
    {
        var list = new List<ChatQuickActionDto>();

        switch (userRole)
        {
            case "Customer":
                list.Add(new ChatQuickActionDto { Label = "My Registered Vehicles", Url = "/Customer/Index", Icon = "bi-car-front" });
                list.Add(new ChatQuickActionDto { Label = "Track Servicing Job", Url = "/Customer/Appointments", Icon = "bi-speedometer2" });
                list.Add(new ChatQuickActionDto { Label = "24/7 Roadside SOS", Url = "/Customer/Roadside", Icon = "bi-shield-exclamation" });
                list.Add(new ChatQuickActionDto { Label = "Warranty Policy", Url = "/Customer/Warranties", Icon = "bi-patch-check" });
                break;

            case "FleetManager":
                list.Add(new ChatQuickActionDto { Label = "PM Overdue Check", Url = "/Fleet/PreventiveMaintenance", Icon = "bi-calendar-check" });
                list.Add(new ChatQuickActionDto { Label = "Fleet Vehicles", Url = "/Fleet/Vehicles", Icon = "bi-truck" });
                list.Add(new ChatQuickActionDto { Label = "Fleet SOP Guide", Url = "#", Icon = "bi-file-text" });
                break;

            case "ServiceAdvisor":
                list.Add(new ChatQuickActionDto { Label = "Workshop Bays", Url = "/ServiceAdvisor/Index", Icon = "bi-grid-3x3-gap" });
                list.Add(new ChatQuickActionDto { Label = "Active Job Cards", Url = "/ServiceAdvisor/Appointments", Icon = "bi-clipboard2-pulse" });
                list.Add(new ChatQuickActionDto { Label = "Intake FAQ", Url = "#", Icon = "bi-question-circle" });
                break;

            case "Mechanic":
                list.Add(new ChatQuickActionDto { Label = "My Assigned Jobs", Url = "/Mechanic/Index", Icon = "bi-tools" });
                list.Add(new ChatQuickActionDto { Label = "Maintenance Specs", Url = "#", Icon = "bi-book" });
                break;

            case "InventoryManager":
            case "Admin":
                list.Add(new ChatQuickActionDto { Label = "Low Stock Parts", Url = "/Inventory/LowStock", Icon = "bi-box-seam" });
                list.Add(new ChatQuickActionDto { Label = "Service Catalog", Url = "/Admin/ServiceCatalog", Icon = "bi-tags" });
                list.Add(new ChatQuickActionDto { Label = "Manage Knowledge Base", Url = "/Chatbot/KnowledgeBase", Icon = "bi-database" });
                break;

            default:
                list.Add(new ChatQuickActionDto { Label = "Service Packages", Url = "/Home/Index#services", Icon = "bi-box" });
                list.Add(new ChatQuickActionDto { Label = "Emergency Roadside", Url = "#", Icon = "bi-telephone" });
                list.Add(new ChatQuickActionDto { Label = "Warranty Coverage", Url = "#", Icon = "bi-patch-check" });
                break;
        }

        return Task.FromResult(list);
    }

    public async Task<List<ChatMessageHistory>> GetSessionHistoryAsync(string sessionId)
    {
        return await _unitOfWork.Repository<ChatMessageHistory>().Query()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Take(50)
            .ToListAsync();
    }

    public async Task ClearSessionHistoryAsync(string sessionId)
    {
        var messages = await _unitOfWork.Repository<ChatMessageHistory>().Query().Where(m => m.SessionId == sessionId).ToListAsync();
        if (messages.Count > 0)
        {
            foreach (var m in messages)
            {
                await _unitOfWork.Repository<ChatMessageHistory>().DeleteAsync(m);
            }
            await _unitOfWork.SaveChangesAsync();
        }
    }

    private string SynthesizeResponse(string query, string intent, string userRole, List<RagChunkMatchDto> docChunks, DatabaseRagResult dbRag)
    {
        var sb = new StringBuilder();

        // 1. Live Database Grounding Facts
        if (dbRag.ContextSummaries.Count > 0)
        {
            sb.AppendLine("### 📋 Current Database Records:");
            foreach (var fact in dbRag.ContextSummaries.Take(4))
            {
                sb.AppendLine($"- {fact}");
            }
            sb.AppendLine();
        }

        // 2. Document Knowledge Chunks
        if (docChunks.Count > 0)
        {
            var bestDoc = docChunks.First();
            sb.AppendLine($"### 📖 From **{bestDoc.DocumentTitle}** (*{bestDoc.SectionTitle}*):");
            sb.AppendLine(bestDoc.ChunkText.Trim());
            sb.AppendLine();

            // If there's another high-relevance chunk from another document or section
            if (docChunks.Count > 1 && docChunks[1].Score >= bestDoc.Score * 0.7)
            {
                var secondDoc = docChunks[1];
                sb.AppendLine($"#### 📌 Additional Details (*{secondDoc.DocumentTitle}*):");
                sb.AppendLine(secondDoc.ChunkText.Trim());
                sb.AppendLine();
            }
        }

        // 3. Fallback when neither document nor DB matches
        if (docChunks.Count == 0 && dbRag.ContextSummaries.Count == 0)
        {
            sb.AppendLine("I could not find a specific match in our service manuals or your database records for that query.");
            sb.AppendLine("\n**Here is what you can ask me:**");
            sb.AppendLine("- **Vehicle & Service Status**: *\"Show my vehicles\"*, *\"Track my job card\"*, *\"Check my invoice\"*");
            sb.AppendLine("- **Policies & Maintenance**: *\"What is covered under warranty?\"*, *\"What are your 24/7 roadside assistance numbers?\"*, *\"When should I change engine oil?\"*");
            sb.AppendLine("- **Workshop & Fleet Operations**: *\"Which fleet vehicles need preventive maintenance?\"*, *\"Check low stock inventory\"*, *\"What service packages do you offer?\"*");
        }

        return sb.ToString().Trim();
    }

    private void PopulateDynamicActionsAndFollowups(ChatResponseDto response, string userRole, string lower)
    {
        if (lower.Contains("oil") || lower.Contains("brake") || lower.Contains("service") || lower.Contains("maintenance"))
        {
            response.SuggestedQuestions.Add("What is included in the Comprehensive Service Package?");
            response.SuggestedQuestions.Add("What is your standard labor warranty?");
            if (userRole == "Customer")
            {
                response.SuggestedActions.Add(new ChatQuickActionDto { Label = "Book Service Appointment", Url = "/Customer/BookService", Icon = "bi-calendar-plus" });
            }
        }
        else if (lower.Contains("warranty") || lower.Contains("guarantee") || lower.Contains("claim"))
        {
            response.SuggestedQuestions.Add("How long is battery replacement warranty?");
            response.SuggestedQuestions.Add("What components are covered under warranty?");
            response.SuggestedQuestions.Add("How do I file a warranty claim?");
            if (userRole == "Customer")
            {
                response.SuggestedActions.Add(new ChatQuickActionDto { Label = "Submit Warranty Claim", Url = "/Customer/Warranties", Icon = "bi-shield-plus" });
            }
        }
        else if (lower.Contains("roadside") || lower.Contains("tow") || lower.Contains("breakdown") || lower.Contains("sos"))
        {
            response.SuggestedQuestions.Add("What is the free towing radius limit?");
            response.SuggestedQuestions.Add("What on-spot assistance services are provided?");
            response.SuggestedActions.Add(new ChatQuickActionDto { Label = "Emergency SOS Hotline: 1800-AUTOPRO", Url = "tel:18002886776", Icon = "bi-telephone-fill" });
        }
        else if (lower.Contains("vehicle") || lower.Contains("car"))
        {
            response.SuggestedQuestions.Add("When is my next preventive maintenance due?");
            response.SuggestedQuestions.Add("Do I have any pending repair estimates?");
        }
        else if (lower.Contains("job") || lower.Contains("assigned") || lower.Contains("workstation"))
        {
            response.SuggestedQuestions.Add("What is the minimum brake pad thickness?");
            response.SuggestedQuestions.Add("What are the engine oil specifications for routine service?");
            if (userRole == "Mechanic")
            {
                response.SuggestedActions.Add(new ChatQuickActionDto { Label = "Mechanic Workstation", Url = "/Mechanic/Index", Icon = "bi-wrench-adjustable" });
            }
        }
        else
        {
            response.SuggestedQuestions = GetDefaultFollowups(userRole);
        }
    }

    private static List<string> GetDefaultFollowups(string userRole)
    {
        return userRole switch
        {
            "Customer" => new() { "What vehicles do I have registered?", "What is covered under warranty?", "What are your roadside assistance numbers?" },
            "FleetManager" => new() { "Which fleet vehicles need maintenance?", "What are the Fleet PM-A and PM-B intervals?", "Show total fleet vehicles" },
            "Mechanic" => new() { "Show my active assigned job cards", "What is the minimum brake pad thickness?", "Engine oil specifications" },
            "ServiceAdvisor" or "Admin" => new() { "Which spare parts are low in stock?", "What are the workshop bay capacities?", "Show service package prices" },
            _ => new() { "What service packages do you offer?", "What is the standard warranty policy?", "How does the digital estimate approval work?" }
        };
    }

    private static bool IsGreeting(string text)
    {
        string cleaned = System.Text.RegularExpressions.Regex.Replace(text.ToLower().Trim(), @"[^\w\s]", " ");
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        if (words.Length <= 5 && (words.Contains("hi") || words.Contains("hello") || words.Contains("hey") || words.Contains("help") || (words.Contains("who") && words.Contains("you"))))
            return true;
        var greetings = new[] { "hi", "hello", "hey", "start", "greetings", "good morning", "good evening", "good afternoon", "help", "who are you" };
        return greetings.Any(g => cleaned.StartsWith(g) || cleaned.EndsWith(g));
    }

    private static bool IsPureAccountOrDbQuery(string lower)
    {
        bool isAskingRecord = lower.Contains("booking") || lower.Contains("appointment") || 
                              lower.Contains("my vehicle") || lower.Contains("my car") || 
                              lower.Contains("registered vehicle") || lower.Contains("garage") ||
                              lower.Contains("my invoice") || lower.Contains("my bill") ||
                              lower.Contains("my estimate") || lower.Contains("track job") || 
                              lower.Contains("job card") || lower.Contains("assigned") ||
                              lower.Contains("my job") || lower.Contains("assigned job") ||
                              lower.Contains("workstation") || lower.Contains("workbench") ||
                              lower.Contains("warranty history") || lower.Contains("my warranty") ||
                              lower.Contains("my warranties") || lower.Contains("warranties") ||
                              lower.Contains("claim history") || lower.Contains("my claim") ||
                              lower.Contains("my claims") || lower.Contains("warranty claim") ||
                              lower.Contains("claims history") || lower.Contains("protection plan") ||
                              lower.Contains("low stock") || lower.Contains("bays");

        bool isAskingGuidelines = lower.Contains("how to") || lower.Contains("what is") || 
                                  lower.Contains("can i") || lower.Contains("eligible") || lower.Contains("eligibility") ||
                                  lower.Contains("claim warranty") || lower.Contains("warranty policy") ||
                                  lower.Contains("policy") || lower.Contains("guide") || 
                                  lower.Contains("manual") || lower.Contains("terms") || 
                                  lower.Contains("warranty covered") || lower.Contains("covered under warranty") ||
                                  lower.Contains("how to claim") || lower.Contains("claim process") || 
                                  lower.Contains("file a claim") || lower.Contains("how to file") ||
                                  lower.Contains("replace") || lower.Contains("fluid") || 
                                  lower.Contains("oil spec") || lower.Contains("limit") ||
                                  lower.Contains("specs") || lower.Contains("protocol");

        return isAskingRecord && !isAskingGuidelines;
    }

    private static string TruncateSnippet(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string oneLine = text.Replace("\r\n", " ").Replace("\n", " ").Trim();
        if (oneLine.Length <= maxChars) return oneLine;
        return oneLine.Substring(0, maxChars) + "...";
    }

    private async Task SaveHistoryAsync(string sessionId, string? userId, string userRole, string message, string response, List<ChatSourceCitationDto> sources)
    {
        try
        {
            var history = new ChatMessageHistory
            {
                SessionId = sessionId,
                UserId = userId,
                UserRole = userRole,
                UserMessage = message,
                BotResponse = response,
                MatchedSourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources) : null,
                Timestamp = DateTime.UtcNow
            };
            await _unitOfWork.Repository<ChatMessageHistory>().AddAsync(history);
            await _unitOfWork.SaveChangesAsync();
        }
        catch
        {
            // Non-critical background failure protection
        }
    }
}
