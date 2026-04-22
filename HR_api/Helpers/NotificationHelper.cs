using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Notification;

namespace HR_api.Helpers;

public class NotificationHelper
{
    private readonly OracleService _oracleService;

    public NotificationHelper(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    public async Task<decimal> SendNotificationAsync(SendNotificationRequest model)
    {
        try
        {
            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE)
                VALUES (:TITLE, :BODY, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            
            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE", model.TITLE),
                new OracleParameter("BODY", model.BODY),
                new OracleParameter("NOTI_TYPE", model.NOTI_TYPE),
                new OracleParameter("TARGET_VAL", model.TARGET_VAL),
                new OracleParameter("LINK_ACTION", (object?)model.LINK_ACTION ?? DBNull.Value),
                new OracleParameter("CREATED_BY", (object?)model.CREATED_BY ?? DBNull.Value),
                outIdParam);

            decimal notiId = 0;
            if (outIdParam.Value != null && outIdParam.Value != DBNull.Value)
            {
                notiId = (decimal)outIdParam.Value;
            }

            // TODO: Sau này gọi Firebase SDK tại đây
            // await SendToFirebaseAsync(model);

            return notiId;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
