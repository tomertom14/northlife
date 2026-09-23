using NorthLife.Api.Models;

namespace NorthLife.Api.Images;

public static class EventImageAccessPolicy
{
    public static bool CanManage(EventImage image, Guid userId, bool isAdmin) =>
        isAdmin || image.UploaderId == userId;
}
