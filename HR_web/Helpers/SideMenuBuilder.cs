using HR_web.Models;
using HR_web.Models.Account;

namespace HR_web.Helpers;

public static class SideMenuBuilder
{
    public static List<SideMenuItem> Build(UserInfoModel? user, bool isMobileApp = false)
    {
        if (user == null) return new List<SideMenuItem>();

        bool isAdmin      = !isMobileApp && user.RoleName == "Admin";
        bool isClerk      = !isMobileApp && user.RoleName == "Clerk";
        bool isHR         = !isMobileApp && user.RoleName == "HR";
        bool isSupervisor = !isMobileApp && user.RoleName == "Supervisor";
        bool isManager    = !isMobileApp && user.RoleName == "Manager";

        return new List<SideMenuItem>
        {
            new SideMenuItem
            {
                Id = "Home",
                Title = "Trang chủ",
                Icon = "home",
                VisibleWhen = () => true,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Tổng quan", Url = "~/Home/Index", Icon = "dashboard" },
                }
            },
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
