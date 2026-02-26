using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

namespace invoice_v1.src.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly JwtOptions _jwtOptions;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            IJwtTokenService jwtTokenService,
            IOptions<JwtOptions> jwtOptions,
            ILogger<AuthService> logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _jwtTokenService = jwtTokenService;
            _jwtOptions = jwtOptions.Value;
            _logger = logger;
        }

        public async Task SignupAsync(SignupRequest request)
        {
            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                if (existingUser.IsSoftDeleted)
                {
                    throw new InvalidOperationException("Prohibited to register contact admin");
                }
                throw new InvalidOperationException("Email is already registered");
            }

            var (hash, salt) = _passwordHasher.HashPassword(request.Password);

            // First user becomes Admin, others are Vendors
            var isFirstUser = !await _userRepository.AnyAdminExistsAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email.ToLower(),
                Username = request.Email.Split('@')[0], // Generate a default username

                // --- FIX: Store the Company Name ---
                CompanyName = request.CompanyName,
                // -----------------------------------

                PasswordHash = hash,
                PasswordSalt = salt,
                Role = isFirstUser ? UserRole.Admin : UserRole.Vendor,
                Status = isFirstUser ? UserStatus.Approved : UserStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.CreateAsync(user);
            await _userRepository.SaveChangesAsync();

            _logger.LogInformation(
                "New user registered: {Email} (Role: {Role}, Company: {Company})",
                request.Email,
                user.Role,
                user.CompanyName);
        }

        public async Task<LoginResult> LoginAsync(LoginRequest request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);

            if (user == null || !_passwordHasher.VerifyPassword(
                request.Password,
                user.PasswordHash,
                user.PasswordSalt))
            {
                throw new UnauthorizedAccessException("Invalid email or password");
            }

            if (user.IsSoftDeleted)
            {
                throw new UnauthorizedAccessException("Account has been deleted");
            }

            if (user.Status == UserStatus.Locked)
            {
                throw new UnauthorizedAccessException("Account is locked. Contact admin to unlock.");
            }

            if (user.Status == UserStatus.Pending)
            {
                throw new UnauthorizedAccessException("Account is pending approval");
            }

            if (user.Status == UserStatus.Rejected)
            {
                throw new UnauthorizedAccessException(
                    $"Account registration was rejected. Reason: {user.RejectionReason ?? "Not specified"}");
            }

            // Update login stats
            user.LastLoginAt = DateTime.UtcNow;
            user.FailedLoginCount = 0; // Reset failed attempts on success
            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var token = _jwtTokenService.GenerateAccessToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes);

            _logger.LogInformation("User {Email} logged in successfully", user.Email);

            // --- FIX: Map UserDto to return with LoginResult ---
            var userDto = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                CompanyName = user.CompanyName,
                Role = user.Role.ToString(),
                Status = user.Status.ToString()
            };

            return new LoginResult
            {
                AccessToken = token,
                ExpiresAt = expiresAt,
                User = userDto // Assign the user details
            };
        }
    }
}
