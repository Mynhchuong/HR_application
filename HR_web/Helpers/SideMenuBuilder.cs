using HR_web.Models;
using HR_web.Models.Account;

namespace HR_web.Helpers;

/// <summary>
/// SideMenuBuilder - giữ nguyên 100% logic, chỉ đổi namespace.
/// URL dạng ~/OT/... vẫn hoạt động trong Razor View .NET 8.
/// </summary>
public static class SideMenuBuilder
{
    public static List<SideMenuItem> Build(UserInfoModel? user)
    {
        if (user == null) return new List<SideMenuItem>();

        bool isAdmin      = user.RoleName == "Admin";
        bool isClerk      = user.RoleName == "Clerk";
        bool isHR         = user.RoleName == "HR";
        bool isSupervisor = user.RoleName == "Supervisor";
        bool isManager    = user.RoleName == "Manager";

        return new List<SideMenuItem>
        {
            // ──────── CÁ NHÂN (tất cả mọi người) ────────
            new SideMenuItem
            {
                Id = "Worker",
                Title = "Cá nhân",
                Icon = "person",
                VisibleWhen = () => true,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Xác nhận Tăng ca",  Url = "~/OT/OtConfirmForm", Icon = "fact_check" },
                    new SideMenuItem { Title = "Phiếu lương",        Url = "~/Payslip/Index",    Icon = "payments"   },
                }
            },

            // ──────── THƯ KÝ ────────
            new SideMenuItem
            {
                Id = "Clerk",
                Title = "Thư ký",
                Icon = "assignment",
                VisibleWhen = () => isClerk || isAdmin,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Danh sách Tăng ca", Url = "~/OT/OtListForClerk", Icon = "view_list" },
                }
            },

            // ──────── QUẢN LÝ ────────
            new SideMenuItem
            {
                Id = "Manager",
                Title = "Quản lý",
                Icon = "supervisor_account",
                VisibleWhen = () => isManager || isSupervisor || isAdmin,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Danh sách Tăng ca", Url = "~/OT/OtListForClerk", Icon = "view_list" },
                }
            },

            // ──────── NHÂN SỰ ────────
            new SideMenuItem
            {
                Id = "HR",
                Title = "Nhân sự",
                Icon = "groups",
                VisibleWhen = () => isHR || isAdmin,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Quản lý Tài khoản",   Url = "~/User/UserManager",   Icon = "manage_accounts"       },
                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForHR",     Icon = "view_list"             },
                    new SideMenuItem { Title = "Quản lý Phiếu lương",  Url = "~/Payslip/Admin",      Icon = "account_balance_wallet" },
                }
            },
        };
    }
}
