using TiToAI.Contracts.Browser;

namespace TiToAI.Browser.Abstractions;

/// <summary>
/// Điều phối quy tắc đăng nhập duy nhất trên toàn ứng dụng (Global One-Visible-Login Rule).
/// Chỉ cho phép tối đa 1 cửa sổ trình duyệt hiển thị (Headless = false) trên desktop tại một thời điểm.
/// </summary>
public interface ILoginCoordinator
{
    /// <summary>
    /// ID của profile hiện đang nắm giữ cửa sổ đăng nhập hiển thị (hoặc null nếu chưa có).
    /// </summary>
    string? CurrentVisibleProfileId { get; }

    /// <summary>
    /// Cho biết hiện có cửa sổ đăng nhập nào đang hiển thị trên desktop hay không.
    /// </summary>
    bool IsVisibleLoginActive { get; }

    /// <summary>
    /// Yêu cầu cấp slot đăng nhập hiển thị cho profileId.
    /// Nếu slot đang bận, yêu cầu được xếp hàng với trạng thái VisibleWaiting.
    /// </summary>
    Task<BrowserRequestLoginResponseData> RequestLoginAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy yêu cầu đăng nhập đang chờ trong hàng đợi.
    /// </summary>
    Task<BrowserCancelLoginResponseData> CancelLoginAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đóng cửa sổ đăng nhập hiển thị, giải phóng Semaphore toàn cục và phát sự kiện trạng thái kèm mã lý do.
    /// Tuyệt đối không tự ý mở lại cửa sổ sau khi người dùng đóng.
    /// </summary>
    Task CloseLoginWindowAsync(
        string profileId,
        string reasonCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chờ đợi cho đến khi profileId nhận được slot hiển thị hoặc timeout.
    /// </summary>
    Task<bool> WaitForVisibleSlotAsync(
        string profileId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Giải phóng quyền sở hữu slot đăng nhập hiển thị của profileId.
    /// </summary>
    void ReleaseVisibleSlot(string profileId);
}
