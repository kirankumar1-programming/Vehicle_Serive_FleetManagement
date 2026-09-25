using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;

namespace VehicleService.Web.Controllers;

public class ChatbotController : Controller
{
    private readonly IRagOrchestratorService _ragService;
    private readonly IDocumentKnowledgeService _docService;
    private readonly IChatbotService _chatbotService;

    public ChatbotController(
        IRagOrchestratorService ragService,
        IDocumentKnowledgeService docService,
        IChatbotService chatbotService)
    {
        _ragService = ragService;
        _docService = docService;
        _chatbotService = chatbotService;
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Query([FromBody] ChatRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty." });
        }

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        string userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Guest";

        var response = await _ragService.ProcessQueryAsync(request.Message, userId, userRole, request.SessionId, request.ContextUrl);
        return Ok(response);
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions()
    {
        string userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Guest";
        var prompts = await _chatbotService.GetQuickPromptsForRoleAsync(userRole);
        return Ok(prompts);
    }

    [HttpGet]
    public async Task<IActionResult> History([FromQuery] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Ok(new object[] { });
        var history = await _chatbotService.GetSessionHistoryAsync(sessionId);
        return Ok(history);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ClearHistory([FromBody] ChatRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request?.SessionId))
        {
            await _chatbotService.ClearSessionHistoryAsync(request.SessionId);
        }
        return Ok(new { success = true });
    }

    [HttpGet]
    [Authorize(Roles = "Administrator,Admin,ServiceAdvisor")]
    public async Task<IActionResult> KnowledgeBase()
    {
        var docs = await _docService.GetAllDocumentsAsync();
        return View(docs);
    }

    [HttpPost]
    [Authorize(Roles = "Administrator,Admin,ServiceAdvisor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadDto dto, IFormFile? file)
    {
        if (file != null && file.Length > 0)
        {
            using var reader = new StreamReader(file.OpenReadStream());
            dto.Content = await reader.ReadToEndAsync();
            dto.FileName = file.FileName;
            dto.FileType = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                dto.Title = Path.GetFileNameWithoutExtension(file.FileName);
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            TempData["ErrorMessage"] = "Document content cannot be empty.";
            return RedirectToAction(nameof(KnowledgeBase));
        }

        await _docService.IngestDocumentAsync(dto);
        TempData["SuccessMessage"] = $"Document '{dto.Title}' successfully ingested and indexed into RAG Knowledge Base!";
        return RedirectToAction(nameof(KnowledgeBase));
    }

    [HttpPost]
    [Authorize(Roles = "Administrator,Admin,ServiceAdvisor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        bool success = await _docService.DeleteDocumentAsync(id);
        if (success)
        {
            TempData["SuccessMessage"] = "Document deleted and removed from RAG index.";
        }
        else
        {
            TempData["ErrorMessage"] = "Document not found.";
        }
        return RedirectToAction(nameof(KnowledgeBase));
    }

    [HttpPost]
    [Authorize(Roles = "Administrator,Admin,ServiceAdvisor")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> TestRag([FromBody] ChatRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
        {
            return BadRequest(new { error = "Query is required" });
        }

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        string userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Administrator";

        var testResult = await _ragService.TestRagPipelineAsync(request.Message, userId, userRole);
        return Ok(testResult);
    }
}
