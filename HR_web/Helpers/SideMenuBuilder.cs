using HR_web.Models;
using HR_web.Models.Account;

namespace HR_web.Helpers;

public static class SideMenuBuilder
{
    public static List<SideMenuItem> Build(UserInfoModel? user, bool isMobileApp = false, bool isActiveTeacher = false)
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
        bool isCSR           = user.RoleName == "CSR";

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
                    new SideMenuItem { Title = "Thông báo",           Url = "~/Notification/Index",   Icon = "notifications", BadgeId = "sideBellBadge" },
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
                    new SideMenuItem { Title = "Xác nhận Tăng ca",   Url = "~/OT/OtConfirmForm",                Icon = "fact_check",   BadgeId = "sideOtBadge" },
                    new SideMenuItem { Title = "Xác nhận chấm công", Url = "~/AttendanceConfirm/WorkerForm",    Icon = "schedule",     BadgeId = "sideAttBadge" },
                   // new SideMenuItem { Title = "Xác nhận nhận quà",  Url = "~/Gift/MyGifts",                     Icon = "card_giftcard", BadgeId = "sideGiftBadge" },
                    new SideMenuItem { Title = "Phiếu lương",         Url = "~/Payslip/Index",                   Icon = "payments"      },
                    new SideMenuItem { Title = "Đăng ký ra vào cổng", Url = "~/GatePass/GpMyRequests",           Icon = "door_front"    },
                    new SideMenuItem { Title = "Đơn nghỉ phép",       Url = "~/Leave/LeaveMyRequests",           Icon = "event_busy"    },
                    // "AI SAMHO - CSR" đã gộp vào trong Hộp thư phản ánh (yêu cầu 2026-09-16, gọn menu) —
                    // bỏ mục menu riêng, lối tắt AI giờ nằm ngay trong trang EmployeeInquiry/Index.
                    new SideMenuItem { Title = "Hộp thư phản ánh",             Url = "~/EmployeeInquiry/Index",           Icon = "forum",        BadgeId = "sideInqBadge" },
                    new SideMenuItem { Title = "Đào tạo cá nhân",     Url = "~/Training/Index",                  Icon = "school"        },
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
                    new SideMenuItem { Title = "Home",                Url = "~/Home/Index",                      Icon = "home"           },
                    //new SideMenuItem { Title = "Notifications",       Url = "~/Notification/IndexForExpat",      Icon = "notifications"  },
                   // new SideMenuItem { Title = "Bulletin",            Url = "~/Bulletin/Index",                  Icon = "campaign"       },
                    new SideMenuItem { Title = "OT List",            Url = "~/OT/OtListForExpat",               Icon = "view_list"      },
                    new SideMenuItem { Title = "Attendance Confirm", Url = "~/AttendanceConfirm/IndexForExpat", Icon = "schedule"       },
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
                    new SideMenuItem { Title = "Xác nhận chấm công",   Url = "~/AttendanceConfirm/Index",    Icon = "schedule"       },
                    // Thư ký CHỈ xem báo cáo + nhắc công nhân lãnh quà — không có quyền tạo danh mục/
                    // đợt quà/import/đánh dấu đã phát như HR (chốt 2026-09-19).
                    new SideMenuItem { Title = "Báo cáo Quà",          Url = "~/GiftAdmin/Report",           Icon = "bar_chart"     },
                    new SideMenuItem { Title = "Theo dõi ra vào cổng", Url = "~/GatePass/GpListForClerk",   Icon = "door_front"    },
                    new SideMenuItem { Title = "Danh sách Nghỉ Phép",  Url = "~/Leave/LeaveListForClerk",   Icon = "event_busy"    },
                    new SideMenuItem { Title = "Lịch nghỉ & Cổng",    Url = "~/Leave/TeamCalendar",         Icon = "calendar_month" },
                    new SideMenuItem { Title = "DS nhân viên",          Url = "~/Employee/MyTeam",           Icon = "groups"         },
                    new SideMenuItem { Title = "Log Đổi Món",          Url = "~/CanteenBread/ChangeLog",     Icon = "history"        },
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
                    new SideMenuItem { Title = "Xác nhận chấm công",  Url = "~/AttendanceConfirm/Index",       Icon = "schedule"        },
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
                    new SideMenuItem { Title = "Xác nhận chấm công",  Url = "~/AttendanceConfirm/Index",       Icon = "schedule"        },
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
                    // Menu HR khá dài (23 mục) — HR yêu cầu 2026-09-10 gom cụm theo màu cho lẹ, mỗi
                    // cụm liền nhau + có nhãn màu riêng (xem MenuGroupCatalog).
                    new SideMenuItem { Title = "Log Sắp Lịch Nghỉ",    Url = "~/Leave/LeaveAssignmentLog",   Icon = "assignment_late",        Group = "leave" },
                    new SideMenuItem { Title = "Danh sách Nghỉ phép",  Url = "~/Leave/LeaveListForHR",       Icon = "event_busy",             Group = "leave" },
                    new SideMenuItem { Title = "Sửa Absent Code ERP",  Url = "~/Leave/ErpAbsentManage",      Icon = "edit_note",              Group = "leave" },
                    new SideMenuItem { Title = "DS làm Chủ Nhật",       Url = "~/SundayLeave/Index",           Icon = "wb_sunny",               Group = "leave" },

                    new SideMenuItem { Title = "Danh sách Tăng ca",    Url = "~/OT/OtListForHR",             Icon = "view_list",              Group = "attendance" },
                    new SideMenuItem { Title = "Phiếu Ra Vào",          Url = "~/GatePass/GpListForHR",       Icon = "door_front",             Group = "attendance" },
                    new SideMenuItem { Title = "Xác nhận chấm công",   Url = "~/AttendanceConfirm/Index",    Icon = "schedule",                Group = "attendance" },

                    new SideMenuItem { Title = "Đợt quà",              Url = "~/GiftAdmin/BatchList",        Icon = "card_giftcard",          Group = "gift" },
                    new SideMenuItem { Title = "Danh mục Quà",         Url = "~/GiftAdmin/Catalog",          Icon = "redeem",                 Group = "gift" },
                    new SideMenuItem { Title = "Báo cáo Quà",          Url = "~/GiftAdmin/Report",           Icon = "bar_chart",              Group = "gift" },

                    new SideMenuItem { Title = "Quản lý Thực đơn",     Url = "~/Menu/Manage",                Icon = "restaurant_menu",        Group = "canteen" },
                    new SideMenuItem { Title = "Quản lý Món ăn",       Url = "~/Menu/FoodManage",            Icon = "set_meal",               Group = "canteen" },
                    new SideMenuItem { Title = "Khoá đổi món",         Url = "~/MealLock/Index",                    Icon = "lock_clock",       Group = "canteen" },
                    new SideMenuItem { Title = "Phiếu Bánh",          Url = "~/CanteenBread/BreadQuota",           Icon = "bakery_dining",     Group = "canteen" },
                    new SideMenuItem { Title = "Log Đổi Món",         Url = "~/CanteenBread/ChangeLog",            Icon = "history",           Group = "canteen" },

                    new SideMenuItem { Title = "Chứng chỉ Đào tạo",    Url = "~/TrainingAdmin/Certificates",        Icon = "card_membership", Group = "training" },
                    new SideMenuItem { Title = "Quản lý Đào tạo",      Url = "~/TrainingAdmin/Index",             Icon = "school",             Group = "training" },

                    new SideMenuItem { Title = "Quản lý hội thoại",     Url = "~/HrInquiry/Index",             Icon = "forum",                 Group = "chat" },
                    new SideMenuItem { Title = "Câu trả lời mẫu",       Url = "~/HrInquiry/CannedReplies",     Icon = "quickreply",            Group = "chat" },
                    new SideMenuItem { Title = "Báo cáo hội thoại",     Url = "~/AdminInquiry/Report",        Icon = "bar_chart",              Group = "chat" },

                    new SideMenuItem { Title = "Quản lý Bản tin",       Url = "~/BulletinAdmin/Manage",       Icon = "campaign",               Group = "system" },
                    new SideMenuItem { Title = "Quy định công ty",      Url = "~/Policy/Manage",              Icon = "policy",                 Group = "system" },
                    new SideMenuItem { Title = "Quản lý Tài khoản",   Url = "~/User/UserManager",            Icon = "manage_accounts",          Group = "system" },
                    new SideMenuItem { Title = "Hình minh hoạ Work Cd", Url = "~/WorkCdImage/Index",         Icon = "image",                   Group = "system" },
                    new SideMenuItem { Title = "Quản lý Phiếu lương",  Url = "~/Payslip/Admin",              Icon = "account_balance_wallet",   Group = "system" },
                    new SideMenuItem { Title = "Phân Quyền Phạm Vi",   Url = "~/UserDept/Index",              Icon = "shield",                  Group = "system" },
                    new SideMenuItem { Title = "Cấu hình Trang chủ",    Url = "~/HomeAdmin/Index",             Icon = "home_app_logo",           Group = "system" },
                    new SideMenuItem { Title = "Quản lý Survey",        Url = "~/SurveyAdmin/Index",           Icon = "poll",                   Group = "system" },
                //    new SideMenuItem { Title = "DS miễn làm Survey",    Url = "~/SurveyExempt/Index",          Icon = "person_off"             },
                }
            },

            new SideMenuItem
            {
                Id = "CSR",
                Title = "CSR",
                Icon = "support_agent",
                VisibleWhen = () => isCSR || isAdmin,
                // Menu CSR cũng khá dài — gom cụm theo màu giống menu HR (yêu cầu 2026-09-15),
                // dùng chung MenuGroupCatalog, mỗi cụm liền nhau + có nhãn màu riêng.
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Log Đổi Món",           Url = "~/CanteenBread/ChangeLog",     Icon = "history",         Group = "canteen" },

                    new SideMenuItem { Title = "Chứng chỉ Đào tạo",     Url = "~/TrainingAdmin/Certificates", Icon = "card_membership", Group = "training" },
                    new SideMenuItem { Title = "Quản lý Đào tạo",       Url = "~/TrainingAdmin/Index",        Icon = "school",          Group = "training" },

                    new SideMenuItem { Title = "Quản lý hội thoại",     Url = "~/HrInquiry/Index",            Icon = "forum",           Group = "chat" },
                    new SideMenuItem { Title = "Câu trả lời mẫu",       Url = "~/HrInquiry/CannedReplies",    Icon = "quickreply",      Group = "chat" },
                    new SideMenuItem { Title = "AI SAMHO - CSR",        Url = "~/AiCsrAdmin/Index",           Icon = "smart_toy",       Group = "chat" },
                    new SideMenuItem { Title = "Báo cáo hội thoại",     Url = "~/AdminInquiry/Report",        Icon = "bar_chart",       Group = "chat" },

                    new SideMenuItem { Title = "Quản lý Bản tin",       Url = "~/BulletinAdmin/Manage",       Icon = "campaign",        Group = "system" },
                    new SideMenuItem { Title = "Quy định công ty",      Url = "~/Policy/Manage",              Icon = "policy",          Group = "system" },
                    new SideMenuItem { Title = "Quản lý Survey",        Url = "~/SurveyAdmin/Index",          Icon = "poll",            Group = "system" },
                    new SideMenuItem { Title = "Cấu hình Trang chủ",    Url = "~/HomeAdmin/Index",            Icon = "home_app_logo",   Group = "system" },
                }
            },

            new SideMenuItem
            {
                Id = "Teacher",
                Title = "Giảng dạy",
                Icon = "school",
                VisibleWhen = () => isActiveTeacher && !isMobileApp && !isExpat && !isCanteen,
                Children = new List<SideMenuItem>
                {
                    new SideMenuItem { Title = "Lớp giảng dạy",       Url = "~/TrainingTeach/MyClasses",      Icon = "dashboard"   },
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
                    new() { Title = "Khoá đổi món",      Url = "~/MealLock/Index",            Icon = "lock_clock"    },
                    new() { Title = "Phiếu Bánh",       Url = "~/CanteenBread/BreadQuota",  Icon = "bakery_dining" },
                    new() { Title = "Log Đổi Món",      Url = "~/CanteenBread/ChangeLog",   Icon = "history"       },
                ]
            },

            new()
            {
                Id = "Admin",
                Title = "Quản trị",
                Icon = "admin_panel_settings",
                VisibleWhen = () => isAdmin,
                // Menu Admin cũng khá dài — gom cụm theo màu như menu HR (yêu cầu 2026-09-10).
                Children = [
                    new() { Title = "Sắp Lịch Toàn Công Ty", Url = "~/Leave/AdminAssignLeave",    Icon = "event_available", Group = "leave" },
                    new() { Title = "Theo Dõi Yêu Cầu",      Url = "~/Leave/AdminManageRequests", Icon = "manage_history",  Group = "leave" },

                    new() { Title = "Quản lý Tăng ca",       Url = "~/OT/OtListForAdmin",         Icon = "edit_calendar",   Group = "attendance" },

                    new() { Title = "Danh sách cấp Bánh cố định", Url = "~/CanteenBread/BreadQuota", Icon = "bakery_dining", Group = "canteen" },

                    new() { Title = "Quản lý hội thoại",     Url = "~/AdminInquiry/Index",        Icon = "forum",           Group = "chat" },
                    new() { Title = "AI SAMHO - CSR",        Url = "~/AiCsrAdmin/Index",          Icon = "smart_toy",       Group = "chat" },
                    new() { Title = "Chủ đề hội thoại",      Url = "~/AdminInquiry/Topics",       Icon = "topic",           Group = "chat" },
                    new() { Title = "Câu trả lời mẫu",       Url = "~/AdminInquiry/CannedReplies", Icon = "quickreply",     Group = "chat" },
                    new() { Title = "Báo cáo hội thoại",    Url = "~/AdminInquiry/Report",       Icon = "bar_chart",       Group = "chat" },

                    new() { Title = "Quản lý Hướng dẫn",     Url = "~/Guide/Manage",              Icon = "menu_book",       Group = "system" },
                    new() { Title = "Quản lý Mẫu thông báo",  Url = "~/NotiTemplate/Index",        Icon = "notifications",   Group = "system" },
                    //new() { Title = "Gửi thông báo",          Url = "~/AdminNoti/Create",          Icon = "edit_notifications" },
                    new() { Title = "Thông báo",       Url = "~/AdminNoti/Index",           Icon = "campaign",        Group = "system" },
                    new() { Title = "Tra cứu Danh bạ NV",    Url = "~/Directory/Index",           Icon = "contact_page",    Group = "system" },
                ]
            },
        };
    }
}
