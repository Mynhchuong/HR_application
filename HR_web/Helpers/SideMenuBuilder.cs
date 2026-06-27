using HR_web.Models;
using HR_web.Models.Account;

namespace HR_web.Helpers;

public static class SideMenuBuilder
{
    public static List<SideMenuItem> Build(UserInfoModel? user, bool isMobileApp = false)
    {
        if (user == null) return new List<SideMenuItem>();

        bool isAdmin         = !isMobileApp && user.RoleName == "Admin";
        bool isClerk         = user.RoleName == "Clerk";
        bool isHR            = !isMobileApp && user.RoleName == "HR";
        bool isSupervisor    = user.RoleName == "Supervisor";
        bool isDeputyManager = user.RoleName == "DeputyManager";
        bool isManager       = user.RoleName == "Manager";
        bool isExpat         = user.RoleName == "Expat";
        bool isCanteen       = !isMobileApp && user.RoleName == "Canteen";

        return new List<SideMenuItem>
        {
            new SideMenuItem
            {
                Id = "Home",
                Title = "Trang chủ",
                Icon = "home",
                VisibleWhen = () => !isExpat && !isCanteen,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Tổng quan",           Url = "~/Home/Index",           Icon = "dashboard"     },
                    new SideMenuItem { Title = "Thông báo",           Url = "~/Notification/Index",   Icon = "notifications" },
                    new SideMenuItem { Title = "Thực đơn",   Url = "~/Menu/Today",    Icon = "restaurant"  },
                    new SideMenuItem { Title = "Bản tin",            Url = "~/Bulletin/Index",       Icon = "campaign"      },
                    new SideMenuItem { Title = "Quy định công ty",   Url = "~/Policy/Index",         Icon = "policy"        },
                    new SideMenuItem { Title = "Hướng dẫn sử dụng", Url = "~/Guide/Index",          Icon = "menu_book"     },
                }
            },
            new SideMenuItem
            {
                Id = "Worker",
                Title = "Cá nhân",
                Icon = "person",
                VisibleWhen = () => !isExpat && !isCanteen,
                Children = new List<SideMenuItem>
                {
                    //new SideMenuItem { Title = "Lịch cá nhân",        Url = "~/Calendar/MyCalendar",     Icon = "calendar_month" },
                    new SideMenuItem { Title = "Xác nhận Tăng ca",   Url = "~/OT/OtConfirmForm",                Icon = "fact_check"    },
                    new SideMenuItem { Title = "Phiếu lương",         Url = "~/Payslip/Index",                   Icon = "payments"      },
                    new SideMenuItem { Title = "Đăng ký ra vào cổng", Url = "~/GatePass/GpMyRequests",           Icon = "door_front"    },
                    new SideMenuItem { Title = "Đơn nghỉ phép",       Url = "~/Leave/LeaveMyRequests",           Icon = "event_busy"    },
                    new SideMenuItem { Title = "Hộp thư",             Url = "~/EmployeeInquiry/Index",           Icon = "forum"         },
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
                    new SideMenuItem { Title = "Notifications",       Url = "~/Notification/IndexForExpat",      Icon = "notifications"  },
                    new SideMenuItem { Title = "Bulletin",            Url = "~/Bulletin/Index",                  Icon = "campaign"       },
                    new SideMenuItem { Title = "OT List",            Url = "~/OT/OtListForExpat",               Icon = "view_list"      },
                    new SideMenuItem { Title = "Gate Pass Approval",  Url = "~/GatePass/GpListForExpat",         Icon = "door_front"     },
                    new SideMenuItem { Title = "Leave Approval",      Url = "~/Leave/LeaveApprovalForExpat",     Icon = "event_available"},
                    new SideMenuItem { Title = "Leave & Gate Calendar",          Url = "~/Leave/TeamCalendarForExpat",      Icon = "calendar_month" },
                    new SideMenuItem { Title = "My Team",                       Url = "~/Employee/MyTeam",                 Icon = "groups"         },
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
                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForClerk",          Icon = "view_list"     },
                    new SideMenuItem { Title = "Theo dõi ra vào cổng", Url = "~/GatePass/GpListForClerk",   Icon = "door_front"    },
                    new SideMenuItem { Title = "Danh sách Nghỉ Phép",  Url = "~/Leave/LeaveListForClerk",   Icon = "event_busy"    },
                    new SideMenuItem { Title = "Lịch nghỉ & Cổng",    Url = "~/Leave/TeamCalendar",         Icon = "calendar_month" },
                    new SideMenuItem { Title = "DS nhân viên",          Url = "~/Employee/MyTeam",           Icon = "groups"         },
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
                    new SideMenuItem { Title = "DS nhân viên",          Url = "~/Employee/MyTeam",               Icon = "groups"          },
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
                    new SideMenuItem { Title = "DS nhân viên",          Url = "~/Employee/MyTeam",               Icon = "groups"          },
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
                    new SideMenuItem { Title = "Quản lý Bản tin",       Url = "~/BulletinAdmin/Manage",       Icon = "campaign"               },
                    new SideMenuItem { Title = "Quy định công ty",      Url = "~/Policy/Manage",              Icon = "policy"                 },
                    new SideMenuItem { Title = "Quản lý Thực đơn",     Url = "~/Menu/Manage",                Icon = "restaurant_menu"        },
                    new SideMenuItem { Title = "Quản lý Món ăn",       Url = "~/Menu/FoodManage",            Icon = "set_meal"               },
                    new SideMenuItem { Title = "Khoá đổi món",         Url = "~/MealLock/Index",                    Icon = "lock_clock"             },
                    new SideMenuItem { Title = "Quản lý Tài khoản",   Url = "~/User/UserManager",            Icon = "manage_accounts"        },
                    new SideMenuItem { Title = "Quản lý Phiếu lương",  Url = "~/Payslip/Admin",              Icon = "account_balance_wallet" },
                    new SideMenuItem { Title = "Phân Quyền Phạm Vi",   Url = "~/UserDept/Index",              Icon = "shield"                 },
                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForHR",             Icon = "view_list"              },
                    new SideMenuItem { Title = "Phiếu Ra Vào",          Url = "~/GatePass/GpListForHR",       Icon = "door_front"             },
                    new SideMenuItem { Title = "Log Sắp Lịch Nghỉ",    Url = "~/Leave/LeaveAssignmentLog",   Icon = "assignment_late"        },
                    new SideMenuItem { Title = "Danh sách Nghỉ phép",  Url = "~/Leave/LeaveListForHR",       Icon = "event_busy"             },
                    new SideMenuItem { Title = "DS làm Chủ Nhật",       Url = "~/SundayLeave/Index",           Icon = "wb_sunny"               },
                    new SideMenuItem { Title = "Quản lý hội thoại",     Url = "~/HrInquiry/Index",             Icon = "forum"                  },
                }
            },

            new()
            {
                Id = "Canteen",
                Title = "Nhà bếp",
                Icon = "restaurant_menu",
                VisibleWhen = () => isCanteen || isAdmin,
                Children = [
                    new() { Title = "Thực đơn hôm nay",  Url = "~/Menu/Today",      Icon = "today"         },
                    new() { Title = "Thực đơn tuần",     Url = "~/Menu/ThisWeek",   Icon = "date_range"    },
                    new() { Title = "Quản lý Thực đơn",  Url = "~/Menu/Manage",     Icon = "edit_calendar" },
                    new() { Title = "Quản lý Món ăn",    Url = "~/Menu/FoodManage", Icon = "set_meal"      },
                    new() { Title = "Khoá đổi món",      Url = "~/MealLock/Index",        Icon = "lock_clock"    },
                ]
            },

            new()
            {
                Id = "Admin",
                Title = "Quản trị",
                Icon = "admin_panel_settings",
                VisibleWhen = () => isAdmin,
                Children = [
                    new() { Title = "Sắp Lịch Toàn Công Ty", Url = "~/Leave/AdminAssignLeave",    Icon = "event_available" },
                    new() { Title = "Theo Dõi Yêu Cầu",      Url = "~/Leave/AdminManageRequests", Icon = "manage_history"  },
                    new() { Title = "Quản lý Hướng dẫn",     Url = "~/Guide/Manage",              Icon = "menu_book"       },
                    new() { Title = "Quản lý Mẫu thông báo",  Url = "~/NotiTemplate/Index",        Icon = "notifications"   },
                    //new() { Title = "Gửi thông báo",          Url = "~/AdminNoti/Create",          Icon = "edit_notifications" },
                    new() { Title = "Thông báo",       Url = "~/AdminNoti/Index",           Icon = "campaign"        },
                    new() { Title = "Quản lý hội thoại",     Url = "~/AdminInquiry/Index",        Icon = "forum"           },
                    new() { Title = "Chủ đề hội thoại",      Url = "~/AdminInquiry/Topics",       Icon = "topic"           },
                    new() { Title = "Báo cáo hội thoại",    Url = "~/AdminInquiry/Report",       Icon = "bar_chart"       },
                ]
            },
        };
    }
}
