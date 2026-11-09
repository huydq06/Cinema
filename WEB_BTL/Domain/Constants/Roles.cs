namespace WEB_BTL.Domain.Constants;

/// <summary>Tên vai trò cố định trong hệ thống.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Staff, Customer];
}

/// <summary>Tên policy dùng chung cho MVC và API.</summary>
public static class Policies
{
    public const string CustomerBooking = "CustomerBooking";
    public const string OperateCinema = "OperateCinema";
    public const string ManageCinema = "ManageCinema";
    public const string ViewRevenue = "ViewRevenue";
}
