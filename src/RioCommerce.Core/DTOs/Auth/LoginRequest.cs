namespace RioCommerce.Core.DTOs.Auth;
public record LoginRequest(string EmailOrPhone, string Password);
public record RegisterRequest(string FullName, string Email, string Phone, string Password, string? City, string? CourseInterest, string? VerifyChannel = "email");
public record VerifyRegistrationRequest(string Target, string Code);
public record ResendCodeRequest(string Target, string Channel);
public record ForgotPasswordRequest(string Target, string? Channel = "email");
public record ResetPasswordRequest(string Target, string Code, string NewPassword);
public record AuthResponse(string Token, DateTime ExpiresAt, UserInfo User);
public record UserInfo(Guid Id, string FullName, string? Email, string? Phone, List<string> Roles);
