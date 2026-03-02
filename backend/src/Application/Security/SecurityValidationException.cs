using invoice_v1.src.Domain.Enums;

namespace invoice_v1.src.Application.Security
{
    public class SecurityValidationException : Exception
    {
        public SecurityFailReason FailCode { get; }

        public SecurityValidationException(
            string message,
            SecurityFailReason failCode)
            : base(message)
        {
            FailCode = failCode;
        }
    }
}