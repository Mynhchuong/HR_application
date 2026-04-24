namespace HR_web.Helpers
{
    public static class MobileHelper
    {
        
        public const string AppUserAgent = "MySamhoMobile";

       
        public static bool IsMobileApp(HttpContext context)
        {
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            return userAgent.Contains(AppUserAgent);
        }
    }
}
