using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VehicleService.Application.DTOs;
using VehicleService.Application.Interfaces;
using VehicleService.Domain.Entities;

namespace VehicleService.Application.Services;

public class DocumentKnowledgeService : IDocumentKnowledgeService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "aren't", "as", "at",
        "be", "because", "been", "before", "being", "below", "between", "both", "but", "by", "can", "cannot", "could",
        "did", "do", "does", "doing", "don't", "down", "during", "each", "few", "for", "from", "further", "had", "has",
        "have", "having", "he", "her", "here", "hers", "herself", "him", "himself", "his", "how", "i", "if", "in", "into",
        "is", "it", "its", "itself", "let's", "me", "more", "most", "my", "myself", "no", "nor", "not", "of", "off",
        "on", "once", "only", "or", "other", "ought", "our", "ours", "ourselves", "out", "over", "own", "same", "she",
        "should", "so", "some", "such", "than", "that", "the", "their", "theirs", "them", "themselves", "then", "there",
        "these", "they", "this", "those", "through", "to", "too", "under", "until", "up", "very", "was", "we", "were",
        "what", "when", "where", "which", "while", "who", "whom", "why", "with", "would", "you", "your", "yours"
    };

    public DocumentKnowledgeService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<RagChunkMatchDto>> SearchDocumentChunksAsync(string query, int topK = 4, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<RagChunkMatchDto>();
        }

        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
        {
            return new List<RagChunkMatchDto>();
        }

        // Query active documents and chunks from persistence
        var chunksQuery = _unitOfWork.Repository<DocumentChunk>().Query()
            .Include(c => c.KnowledgeDocument)
            .Where(c => c.KnowledgeDocument != null && c.KnowledgeDocument.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            chunksQuery = chunksQuery.Where(c => c.KnowledgeDocument!.Category == category);
        }

        var allChunks = await chunksQuery.ToListAsync();
        if (allChunks.Count == 0)
        {
            return new List<RagChunkMatchDto>();
        }

        // BM25-inspired ranking parameters
        double k1 = 1.5;
        double b = 0.75;
        double avgLength = allChunks.Average(c => c.ChunkText.Length);
        int totalDocs = allChunks.Count;

        // Calculate document frequency for query terms
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in queryTerms)
        {
            docFreq[term] = allChunks.Count(c =>
                c.ChunkText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.SectionTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var scoredList = new List<RagChunkMatchDto>();

        foreach (var chunk in allChunks)
        {
            double score = 0;
            string lowerChunkText = chunk.ChunkText.ToLower();
            string lowerSection = chunk.SectionTitle.ToLower();
            string lowerDocTitle = chunk.KnowledgeDocument?.Title.ToLower() ?? "";

            // Bonus for full phrase matching
            if (lowerChunkText.Contains(query.ToLower().Trim()))
            {
                score += 5.0;
            }

            foreach (var term in queryTerms)
            {
                int df = docFreq.GetValueOrDefault(term, 0);
                if (df == 0) continue;

                // IDF
                double idf = Math.Log(1.0 + (totalDocs - df + 0.5) / (df + 0.5));
                if (idf < 0) idf = 0.05;

                // Term frequency in chunk
                int tf = CountOccurrences(lowerChunkText, term);

                // Section title and Doc title boost
                if (lowerSection.Contains(term))
                {
                    tf += 3;
                }
                if (lowerDocTitle.Contains(term))
                {
                    tf += 2;
                }

                if (tf > 0)
                {
                    double tfNorm = (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * (chunk.ChunkText.Length / Math.Max(1.0, avgLength))));
                    score += idf * tfNorm;
                }
            }

            if (score > 0.1)
            {
                scoredList.Add(new RagChunkMatchDto
                {
                    DocumentId = chunk.KnowledgeDocumentId,
                    DocumentTitle = chunk.KnowledgeDocument?.Title ?? "Knowledge Document",
                    Category = chunk.KnowledgeDocument?.Category ?? "General",
                    SectionTitle = chunk.SectionTitle,
                    ChunkText = chunk.ChunkText,
                    Score = Math.Round(score, 3)
                });
            }
        }

        return scoredList
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    public async Task<List<DocumentSummaryDto>> GetAllDocumentsAsync()
    {
        return await _unitOfWork.Repository<KnowledgeDocument>().Query()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentSummaryDto
            {
                Id = d.Id,
                Title = d.Title,
                Category = d.Category,
                Description = d.Description,
                FileName = d.FileName,
                ChunkCount = d.ChunkCount,
                CreatedAt = d.CreatedAt,
                IsActive = d.IsActive
            })
            .ToListAsync();
    }

    public async Task<KnowledgeDocument?> GetDocumentByIdAsync(int id)
    {
        return await _unitOfWork.Repository<KnowledgeDocument>().Query()
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<int> IngestDocumentAsync(DocumentUploadDto dto)
    {
        var doc = new KnowledgeDocument
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? dto.FileName : dto.Title.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim(),
            Description = dto.Description?.Trim() ?? string.Empty,
            FileName = string.IsNullOrWhiteSpace(dto.FileName) ? "Uploaded_Document.txt" : dto.FileName.Trim(),
            FileType = dto.FileType,
            RawContent = dto.Content,
            IsActive = true
        };

        var chunks = ChunkContent(doc.RawContent, doc.Title);
        doc.ChunkCount = chunks.Count;

        foreach (var chunk in chunks)
        {
            doc.Chunks.Add(chunk);
        }

        await _unitOfWork.Repository<KnowledgeDocument>().AddAsync(doc);
        await _unitOfWork.SaveChangesAsync();

        return doc.Id;
    }

    public async Task<bool> DeleteDocumentAsync(int id)
    {
        var doc = await _unitOfWork.Repository<KnowledgeDocument>().GetByIdAsync(id);
        if (doc == null) return false;

        await _unitOfWork.Repository<KnowledgeDocument>().DeleteAsync(doc);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task ReindexAllDocumentsAsync()
    {
        var docs = await _unitOfWork.Repository<KnowledgeDocument>().Query().Include(d => d.Chunks).ToListAsync();
        foreach (var doc in docs)
        {
            foreach (var c in doc.Chunks.ToList())
            {
                await _unitOfWork.Repository<DocumentChunk>().DeleteAsync(c);
            }
            var newChunks = ChunkContent(doc.RawContent, doc.Title);
            doc.ChunkCount = newChunks.Count;
            foreach (var chunk in newChunks)
            {
                doc.Chunks.Add(chunk);
            }
            await _unitOfWork.Repository<KnowledgeDocument>().UpdateAsync(doc);
        }
        await _unitOfWork.SaveChangesAsync();
    }

    private static List<DocumentChunk> ChunkContent(string rawContent, string defaultTitle)
    {
        var chunks = new List<DocumentChunk>();
        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        string currentSection = defaultTitle;
        var sectionBuffer = new List<string>();
        int chunkIdx = 0;

        void Flush()
        {
            if (sectionBuffer.Count == 0) return;
            string text = string.Join("\n", sectionBuffer).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var words = Tokenize(text);
            string kw = string.Join(" ", words.Distinct().Take(30));

            chunks.Add(new DocumentChunk
            {
                ChunkIndex = chunkIdx++,
                SectionTitle = currentSection,
                ChunkText = text,
                Keywords = kw
            });
            sectionBuffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("# ") || line.StartsWith("## ") || line.StartsWith("### "))
            {
                Flush();
                currentSection = line.TrimStart('#').Trim();
            }
            else
            {
                sectionBuffer.Add(line);
                if (sectionBuffer.Count >= 25 && string.IsNullOrWhiteSpace(line))
                {
                    Flush();
                }
            }
        }
        Flush();

        // If no markdown headers were present, chunk by paragraphs
        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(rawContent))
        {
            var paragraphs = rawContent.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            int idx = 0;
            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (trimmed.Length < 15) continue;
                chunks.Add(new DocumentChunk
                {
                    ChunkIndex = idx++,
                    SectionTitle = defaultTitle,
                    ChunkText = trimmed,
                    Keywords = string.Join(" ", Tokenize(trimmed).Distinct().Take(30))
                });
            }
        }

        return chunks;
    }

    private static List<string> Tokenize(string text)
    {
        var clean = Regex.Replace(text.ToLower(), @"[^\w\s]", " ");
        return clean.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .ToList();
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int i = 0;
        while ((i = text.IndexOf(pattern, i, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            i += pattern.Length;
            count++;
        }
        return count;
    }
}
