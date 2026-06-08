using HR_web.Models;
using HR_web.Models.Account;

namespace HR_web.Helpers;

public static class SideMenuBuilder
{
    public static List<SideMenuItem> Build(UserInfoModel? user, bool isMobileApp = false)
    {
        if (user == null) return new List<SideMenuItem>();

        bool isAdmin         = !isMobileApp && user.RoleName == "Admin";
        bool isClerk         = !isMobileApp && user.RoleName == "Clerk";
        bool isHR            = !isMobileApp && user.RoleName == "HR";
        bool isSupervisor    = user.RoleName == "Supervisor";
        bool isDeputyManager = user.RoleName == "DeputyManager";
        bool isManager       = user.RoleName == "Manager";
        bool isExpat         = user.RoleName == "Expat";

        return new List<SideMenuItem>
        {
            new SideMenuItem
            {
                Id = "Home",
                Title = "Trang chủ",
                Icon = "home",
                VisibleWhen = () => !isExpat,
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
                VisibleWhen = () => !isExpat,
                Children = new List<SideMenuItem>
                {
                    // new SideMenuItem { Title = "Lịch cá nhân",        Url = "~/Calendar/MyCalendar",     Icon = "calendar_month" },
                    new SideMenuItem { Title = "Xác nhận Tăng ca",   Url = "~/OT/OtConfirmForm",         Icon = "fact_check"    },
                    new SideMenuItem { Title = "Phiếu lương",         Url = "~/Payslip/Index",           Icon = "payments"      },
                    new SideMenuItem { Title = "Đăng ký ra vào cổng", Url = "~/GatePass/GpMyRequests",   Icon = "door_front"    },
                    new SideMenuItem { Title = "Đơn nghỉ phép",       Url = "~/Leave/LeaveMyRequests",   Icon = "event_busy"    },
                    new SideMenuItem { Title = "Quy định công ty",    Url = "~/Policy/Index",            Icon = "policy"        },
                    new SideMenuItem { Title = "Hướng dẫn sử dụng",  Url = "~/Guide/Index",             Icon = "menu_book"     },
                }
            },

            new SideMenuItem
            {
                Id = "Expat",
                Title = "Expat",
                Icon = "manage_accounts",
                VisibleWhen = () => isExpat || isAdmin,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "OT List",            Url = "~/OT/OtListForExpat",               Icon = "view_list"      },
                    new SideMenuItem { Title = "Gate Pass Approval",  Url = "~/GatePass/GpListForExpat",         Icon = "door_front"     },
                    new SideMenuItem { Title = "Leave Approval",      Url = "~/Leave/LeaveApprovalForExpat",     Icon = "event_available"},
                    new SideMenuItem { Title = "Calendar leave & gate pass",   Url = "~/Leave/TeamCalendarForExpat",      Icon = "calendar_month" },
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
                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForClerk",       Icon = "view_list"     },
                    new SideMenuItem { Title = "Theo dõi ra vào cổng", Url = "~/GatePass/GpListForClerk", Icon = "door_front"    },
                    new SideMenuItem { Title = "Lịch nghỉ & Cổng",    Url = "~/Leave/TeamCalendar",      Icon = "calendar_month" },
                }
            },

            new SideMenuItem
            {
                Id = "Supervisor",
                Title = "Giám sát",
                Icon = "engineering",
                VisibleWhen = () => isSupervisor || isAdmin,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Danh sách Tăng ca",   Url = "~/OT/OtListForSupervisor",        Icon = "view_list"      },
                    new SideMenuItem { Title = "Duyệt ra vào cổng",   Url = "~/GatePass/GpListForSupervisor",  Icon = "door_front"     },
                    new SideMenuItem { Title = "Lịch nghỉ & Cổng",   Url = "~/Leave/TeamCalendar",            Icon = "calendar_month" },
                    new SideMenuItem { Title = "Duyệt lịch nghỉ",     Url = "~/Leave/TeamSchedule",            Icon = "event_available" },
                }
            },

            new SideMenuItem
            {
                Id = "Manager",
                Title = "Quản lý",
                Icon = "supervisor_account",
                VisibleWhen = () => isManager || isDeputyManager,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Danh sách Tăng ca",   Url = "~/OT/OtListForSupervisor",        Icon = "view_list"      },
                    new SideMenuItem { Title = "Duyệt ra vào cổng",   Url = "~/GatePass/GpListForSupervisor",  Icon = "door_front"     },
                    new SideMenuItem { Title = "Lịch nghỉ & Cổng",   Url = "~/Leave/TeamCalendar",            Icon = "calendar_month" },
                    new SideMenuItem { Title = "Duyệt lịch nghỉ",     Url = "~/Leave/TeamSchedule",            Icon = "event_available" },
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
                    new SideMenuItem { Title = "Quy định công ty",      Url = "~/Policy/Manage",              Icon = "policy"                 },
                    new SideMenuItem { Title = "Quản lý Tài khoản",   Url = "~/User/UserManager",            Icon = "manage_accounts"        },
                    new SideMenuItem { Title = "Phân Quyền Phạm Vi",   Url = "~/UserDept/Index",              Icon = "shield"                 },
                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForHR",             Icon = "view_list"              },
                    new SideMenuItem { Title = "Quản lý Phiếu lương",  Url = "~/Payslip/Admin",              Icon = "account_balance_wallet" },
                    new SideMenuItem { Title = "Phiếu Ra Vào",          Url = "~/GatePass/GpListForHR",       Icon = "door_front"             },
                    new SideMenuItem { Title = "Log Sắp Lịch Nghỉ",    Url = "~/Leave/LeaveAssignmentLog",   Icon = "assignment_late"        },
                    new SideMenuItem { Title = "Danh sách Nghỉ phép",  Url = "~/Leave/LeaveListForHR",       Icon = "event_busy"             },
                }
            },

            new()
            {
                Id = "Admin",
                Title = "Quản trị",
                Icon = "admin_panel_settings",
                VisibleWhen = () => isAdmin,
                Children = [
                    new() { Title = "Sắp Lịch Toàn Công Ty", Url = "~/Leave/AdminAssignLeave", Icon = "event_available" },
                    new() { Title = "Quản lý Hướng dẫn",     Url = "~/Guide/Manage",           Icon = "menu_book"       },
                ]
            },
        };
    }
}
