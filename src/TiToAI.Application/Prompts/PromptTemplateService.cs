using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Prompts.Exceptions;
using TiToAI.Application.Prompts.Models;
using TiToAI.Contracts.Prompts;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Prompts;

/// <summary>
/// Dịch vụ quản lý Prompt Template Engine, versioning bất biến, workspace override,
/// render preview và snapshot bất biến theo contracts.md v1.0.4.
/// </summary>
public sealed class PromptTemplateService
{
    private static readonly HashSet<string> ValidPurposes = new(StringComparer.Ordinal)
    {
        "Script",
        "Image",
        "VideoPrompt"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TiToAIDbContext _dbContext;

    public PromptTemplateService(TiToAIDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của Purpose.
    /// </summary>
    public static void ValidatePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose) || !ValidPurposes.Contains(purpose))
        {
            throw new PromptValidationException(
                $"Mục đích prompt '{purpose}' không hợp lệ. Các mục đích cho phép: {string.Join(", ", ValidPurposes)}.");
        }
    }

    /// <summary>
    /// Lấy danh sách Prompt Templates.
    /// Nếu workspaceId == null: chỉ lấy Global templates.
    /// Nếu workspaceId != null: lấy Global templates và Override templates của chính Workspace đó.
    /// Nghiêm cấm trả về override của kênh khác.
    /// </summary>
    public async Task<PromptListTemplatesResponseData> ListTemplatesAsync(
        string? workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            bool wsExists = await _dbContext.Workspaces
                .AsNoTracking()
                .AnyAsync(w => w.Id == workspaceId, cancellationToken);

            if (!wsExists)
            {
                throw new PromptSecurityException($"Không tìm thấy Workspace với ID '{workspaceId}'.", workspaceId);
            }
        }

        var query = _dbContext.PromptTemplates
            .AsNoTracking()
            .Include(t => t.CurrentVersion)
            .AsQueryable();

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            // Chỉ lấy global
            query = query.Where(t => t.WorkspaceId == null);
        }
        else
        {
            // Lấy global và override của chính workspaceId này
            query = query.Where(t => t.WorkspaceId == null || t.WorkspaceId == workspaceId);
        }

        var templates = await query
            .OrderBy(t => t.Purpose)
            .ThenByDescending(t => t.WorkspaceId != null) // Overrides lên trước hoặc nhóm
            .ToListAsync(cancellationToken);

        var dtos = templates.Select(t => ToDto(t, t.CurrentVersion?.VersionNumber)).ToList();
        return new PromptListTemplatesResponseData(dtos);
    }

    /// <summary>
    /// Lấy chi tiết Prompt Template theo templateId hoặc purpose + resolution precedence.
    /// </summary>
    public async Task<PromptGetTemplateResponseData> GetTemplateAsync(
        string? templateId,
        string? purpose,
        string? workspaceId,
        CancellationToken cancellationToken = default)
    {
        PromptTemplate? template = null;

        if (!string.IsNullOrWhiteSpace(templateId))
        {
            template = await _dbContext.PromptTemplates
                .AsNoTracking()
                .Include(t => t.CurrentVersion)
                .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

            if (template == null)
            {
                throw new PromptTemplateNotFoundException(templateId);
            }

            // Kiểm tra ranh giới workspace: nếu template là override của kênh khác thì cấm truy cập
            if (template.WorkspaceId != null &&
                !string.IsNullOrWhiteSpace(workspaceId) &&
                !string.Equals(template.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                throw new PromptSecurityException(
                    "Không được phép truy cập template override của Workspace khác.",
                    template.WorkspaceId,
                    workspaceId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(purpose))
        {
            ValidatePurpose(purpose);

            // Resolution Precedence:
            // 1. Nếu có workspaceId: tìm Workspace Override trước
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                template = await _dbContext.PromptTemplates
                    .AsNoTracking()
                    .Include(t => t.CurrentVersion)
                    .FirstOrDefaultAsync(t => t.Purpose == purpose && t.WorkspaceId == workspaceId, cancellationToken);
            }

            // 2. Nếu không tìm thấy override -> Fallback về Global
            if (template == null)
            {
                template = await _dbContext.PromptTemplates
                    .AsNoTracking()
                    .Include(t => t.CurrentVersion)
                    .FirstOrDefaultAsync(t => t.Purpose == purpose && t.WorkspaceId == null, cancellationToken);
            }

            if (template == null)
            {
                throw new PromptTemplateNotFoundException(purpose, workspaceId);
            }
        }
        else
        {
            throw new PromptValidationException("Cần cung cấp ít nhất templateId hoặc purpose để lấy template.");
        }

        var templateDto = ToDto(template, template.CurrentVersion?.VersionNumber);
        var currentVersionDto = template.CurrentVersion != null ? ToVersionDto(template.CurrentVersion) : null;

        return new PromptGetTemplateResponseData(templateDto, currentVersionDto);
    }

    /// <summary>
    /// Lấy danh sách lịch sử các phiên bản của template.
    /// </summary>
    public async Task<PromptListVersionsResponseData> ListVersionsAsync(
        string templateId,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var template = await _dbContext.PromptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template == null)
        {
            throw new PromptTemplateNotFoundException(templateId);
        }

        if (template.WorkspaceId != null &&
            !string.IsNullOrWhiteSpace(workspaceId) &&
            !string.Equals(template.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new PromptSecurityException(
                "Không được phép xem danh sách phiên bản của Workspace khác.",
                template.WorkspaceId,
                workspaceId);
        }

        var versions = await _dbContext.PromptVersions
            .AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        var dtos = versions.Select(ToVersionDto).ToList();
        return new PromptListVersionsResponseData(dtos);
    }

    /// <summary>
    /// Tạo phiên bản mới cho template (bất biến).
    /// Nếu template chưa tồn tại (theo purpose và workspaceId): tạo template mới + version 1.
    /// Nếu template đã tồn tại: tạo version kế tiếp (max + 1) và cập nhật CurrentVersionId.
    /// </summary>
    public async Task<PromptCreateVersionResponseData> CreateVersionAsync(
        PromptCreateVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePurpose(request.Purpose);

        if (string.IsNullOrWhiteSpace(request.TemplateText))
        {
            throw new PromptValidationException("Nội dung template không được để trống.");
        }

        // Validate cú pháp template
        var (detectedVars, malformed) = PromptRenderer.ParseTemplate(request.TemplateText);
        if (malformed.Count > 0)
        {
            throw new PromptValidationException(
                $"Template chứa placeholder lỗi cú pháp: {string.Join(", ", malformed)}");
        }

        // Validate definitions json nếu có
        string definitionsJson = string.IsNullOrWhiteSpace(request.VariableDefinitionsJson)
            ? "[]"
            : request.VariableDefinitionsJson;

        try
        {
            var parsedDefs = JsonSerializer.Deserialize<List<PromptVariableDefinition>>(definitionsJson, JsonOptions);
            if (parsedDefs == null)
            {
                definitionsJson = "[]";
            }
        }
        catch (JsonException ex)
        {
            throw new PromptValidationException($"VariableDefinitionsJson không đúng định dạng JSON hợp lệ: {ex.Message}");
        }

        // Kiểm tra workspace nếu có
        if (!string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            bool wsExists = await _dbContext.Workspaces
                .AsNoTracking()
                .AnyAsync(w => w.Id == request.WorkspaceId, cancellationToken);

            if (!wsExists)
            {
                throw new PromptSecurityException($"Không tìm thấy Workspace với ID '{request.WorkspaceId}'.", request.WorkspaceId);
            }
        }

        string nowUtc = DateTimeOffset.UtcNow.ToString("o");

        // Tìm template hiện có theo purpose và workspaceId
        var template = await _dbContext.PromptTemplates
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t =>
                t.Purpose == request.Purpose &&
                (request.WorkspaceId == null ? t.WorkspaceId == null : t.WorkspaceId == request.WorkspaceId),
                cancellationToken);

        PromptVersion newVersion;

        if (template == null)
        {
            // Tạo template mới
            string templateId = Guid.NewGuid().ToString();
            string versionId = Guid.NewGuid().ToString();

            template = new PromptTemplate
            {
                Id = templateId,
                Purpose = request.Purpose,
                WorkspaceId = request.WorkspaceId,
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            newVersion = new PromptVersion
            {
                Id = versionId,
                TemplateId = templateId,
                VersionNumber = 1,
                TemplateText = request.TemplateText,
                VariableDefinitionsJson = definitionsJson,
                ChangeDescription = request.ChangeDescription ?? "Khởi tạo template",
                CreatedAtUtc = nowUtc
            };

            _dbContext.PromptTemplates.Add(template);
            _dbContext.PromptVersions.Add(newVersion);
            await _dbContext.SaveChangesAsync(cancellationToken);

            template.CurrentVersion = newVersion;
            template.CurrentVersionId = versionId;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Template đã tồn tại, tìm versionNumber lớn nhất
            int maxVersion = await _dbContext.PromptVersions
                .Where(v => v.TemplateId == template.Id)
                .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;

            string versionId = Guid.NewGuid().ToString();

            newVersion = new PromptVersion
            {
                Id = versionId,
                TemplateId = template.Id,
                VersionNumber = maxVersion + 1,
                TemplateText = request.TemplateText,
                VariableDefinitionsJson = definitionsJson,
                ChangeDescription = request.ChangeDescription,
                CreatedAtUtc = nowUtc
            };

            _dbContext.PromptVersions.Add(newVersion);
            await _dbContext.SaveChangesAsync(cancellationToken);

            template.CurrentVersionId = versionId;
            template.CurrentVersion = newVersion;
            template.UpdatedAtUtc = nowUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var templateDto = ToDto(template, newVersion.VersionNumber);
        var versionDto = ToVersionDto(newVersion);

        return new PromptCreateVersionResponseData(templateDto, versionDto);
    }

    /// <summary>
    /// Rollback CurrentVersionId của template trỏ về targetVersionId.
    /// TUYỆT ĐỐI KHÔNG sửa đổi nội dung body hay lịch sử của các version cũ.
    /// </summary>
    public async Task<PromptRollbackVersionResponseData> RollbackVersionAsync(
        PromptRollbackVersionRequest request,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetVersionId);

        var template = await _dbContext.PromptTemplates
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
        {
            throw new PromptTemplateNotFoundException(request.TemplateId);
        }

        if (template.WorkspaceId != null &&
            !string.IsNullOrWhiteSpace(workspaceId) &&
            !string.Equals(template.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new PromptSecurityException(
                "Không được phép rollback template thuộc Workspace khác.",
                template.WorkspaceId,
                workspaceId);
        }

        var targetVersion = await _dbContext.PromptVersions
            .FirstOrDefaultAsync(v => v.Id == request.TargetVersionId, cancellationToken);

        if (targetVersion == null)
        {
            throw new PromptVersionNotFoundException(request.TargetVersionId, template.Id);
        }

        if (!string.Equals(targetVersion.TemplateId, template.Id, StringComparison.Ordinal))
        {
            throw new PromptRollbackException(
                template.Id,
                request.TargetVersionId,
                $"Phiên bản '{request.TargetVersionId}' không thuộc template '{template.Id}'.");
        }

        // Cập nhật con trỏ current version, không sửa body lịch sử
        template.CurrentVersionId = targetVersion.Id;
        template.CurrentVersion = targetVersion;
        template.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o");

        await _dbContext.SaveChangesAsync(cancellationToken);

        var templateDto = ToDto(template, targetVersion.VersionNumber);
        var versionDto = ToVersionDto(targetVersion);

        return new PromptRollbackVersionResponseData(templateDto, versionDto);
    }

    /// <summary>
    /// Xóa bản ghi override của Workspace. Cascade xóa các version của override đó.
    /// Kênh quay về sử dụng Global template mặc định.
    /// Cấm xóa Global template bằng phương thức này.
    /// </summary>
    public async Task<PromptDeleteOverrideResponseData> DeleteOverrideAsync(
        PromptDeleteOverrideRequest request,
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateId);

        var template = await _dbContext.PromptTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
        {
            throw new PromptTemplateNotFoundException(request.TemplateId);
        }

        if (template.WorkspaceId == null)
        {
            throw new PromptOverrideException(
                template.Id,
                "Không thể xóa Global template bằng lệnh delete_override. Lệnh này chỉ áp dụng cho Workspace Override.");
        }

        if (!string.IsNullOrWhiteSpace(workspaceId) &&
            !string.Equals(template.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new PromptSecurityException(
                "Không được phép xóa template override của Workspace khác.",
                template.WorkspaceId,
                workspaceId);
        }

        // Ngắt con trỏ CurrentVersionId trước để phá vỡ circular dependency giữa PromptTemplate và PromptVersion
        template.CurrentVersionId = null;
        template.CurrentVersion = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Xóa template (cascade delete sẽ xóa toàn bộ version)
        _dbContext.PromptTemplates.Remove(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PromptDeleteOverrideResponseData(request.TemplateId, true);
    }

    /// <summary>
    /// Thẩm định và Render Preview cho prompt template.
    /// Hỗ trợ cả preview văn bản nháp (editor draft) lẫn preview từ template đã lưu.
    /// </summary>
    public async Task<PromptPreviewResponseData> PreviewAsync(
        PromptPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string templateTextToRender;
        string? resolvedTemplateId = request.TemplateId;
        string? resolvedVersionId = null;
        bool isOverride = false;
        List<PromptVariableDefinition>? definitions = null;

        if (!string.IsNullOrWhiteSpace(request.TemplateText))
        {
            // Sử dụng văn bản nháp được truyền lên trực tiếp
            templateTextToRender = request.TemplateText;

            if (!string.IsNullOrWhiteSpace(request.TemplateId))
            {
                var template = await _dbContext.PromptTemplates
                    .AsNoTracking()
                    .Include(t => t.CurrentVersion)
                    .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

                if (template != null)
                {
                    isOverride = template.WorkspaceId != null;
                    resolvedVersionId = template.CurrentVersionId;
                    definitions = ParseDefinitions(template.CurrentVersion?.VariableDefinitionsJson);
                }
            }
        }
        else
        {
            // Phân giải từ cơ sở dữ liệu
            var getResult = await GetTemplateAsync(
                request.TemplateId,
                request.Purpose,
                request.WorkspaceId,
                cancellationToken);

            var template = getResult.Template;
            var currentVersion = getResult.CurrentVersion;

            if (currentVersion == null || string.IsNullOrWhiteSpace(currentVersion.TemplateText))
            {
                throw new PromptValidationException("Template chưa có phiên bản hoặc nội dung template rỗng.");
            }

            templateTextToRender = currentVersion.TemplateText;
            resolvedTemplateId = template.TemplateId;
            resolvedVersionId = currentVersion.VersionId;
            isOverride = template.IsOverride;
            definitions = ParseDefinitions(currentVersion.VariableDefinitionsJson);
        }

        string renderedText = PromptRenderer.Render(templateTextToRender, request.Variables, definitions);
        var (detectedVars, _) = PromptRenderer.ParseTemplate(templateTextToRender);

        return new PromptPreviewResponseData(
            RenderedText: renderedText,
            TemplateId: resolvedTemplateId,
            VersionId: resolvedVersionId,
            VariableCount: detectedVars.Count,
            IsOverride: isOverride);
    }

    /// <summary>
    /// Tạo bản chụp bất biến (PromptRenderSnapshotDto) phục vụ cho T15 Job Queue.
    /// Bất biến hoàn toàn trước các thay đổi hoặc rollback template sau này.
    /// </summary>
    public async Task<PromptRenderSnapshotDto> CreateSnapshotAsync(
        string? templateId,
        string? purpose,
        string? workspaceId,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(
            new PromptPreviewRequest(
                TemplateId: templateId,
                Purpose: purpose,
                WorkspaceId: workspaceId,
                TemplateText: null,
                Variables: variables),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(preview.TemplateId) || string.IsNullOrWhiteSpace(preview.VersionId))
        {
            throw new PromptValidationException("Không thể tạo snapshot bất biến khi thiếu templateId hoặc versionId.");
        }

        var snapshot = new PromptRenderSnapshot(
            templateId: preview.TemplateId,
            versionId: preview.VersionId,
            renderedText: preview.RenderedText,
            variables: variables,
            capturedAtUtc: DateTimeOffset.UtcNow.ToString("o"));

        return snapshot.ToDto();
    }

    private static List<PromptVariableDefinition>? ParseDefinitions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<PromptVariableDefinition>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static PromptTemplateDto ToDto(PromptTemplate template, int? currentVersionNumber = null)
    {
        return new PromptTemplateDto(
            TemplateId: template.Id,
            Purpose: template.Purpose,
            WorkspaceId: template.WorkspaceId,
            IsOverride: template.WorkspaceId != null,
            CurrentVersionId: template.CurrentVersionId,
            CurrentVersionNumber: currentVersionNumber ?? template.CurrentVersion?.VersionNumber,
            CreatedAtUtc: template.CreatedAtUtc,
            UpdatedAtUtc: template.UpdatedAtUtc);
    }

    public static PromptVersionDto ToVersionDto(PromptVersion version)
    {
        return new PromptVersionDto(
            VersionId: version.Id,
            TemplateId: version.TemplateId,
            VersionNumber: version.VersionNumber,
            TemplateText: version.TemplateText,
            VariableDefinitionsJson: version.VariableDefinitionsJson,
            ChangeDescription: version.ChangeDescription,
            CreatedAtUtc: version.CreatedAtUtc);
    }
}
