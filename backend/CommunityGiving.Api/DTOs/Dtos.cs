namespace CommunityGiving.Api.DTOs;

// ---------- Auth ----------
public record RegisterRequest(string FullName, string Email, string Password, string Phone);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAtUtc, string FullName, string Email, string[] Roles, int? MemberId);

// ---------- Organization settings (branding & vocabulary, configurable per deployment) ----------
public record OrganizationSettingsDto(string Name, string Type, string Tagline, string ContactEmail,
    string ContactPhone, string Address, string? LogoUrl, string Currency, string ProgramsLabel, bool ProgramsEnabled);

public record UpdateOrganizationSettingsRequest(string Name, string Type, string Tagline, string ContactEmail,
    string ContactPhone, string Address, string? LogoUrl, string Currency, string ProgramsLabel, bool ProgramsEnabled,
    string ReceiptFooterText);

// ---------- Members / Households ----------
public record MemberDto(int Id, string FirstName, string LastName, string Email, string Phone,
    string RoleInHousehold, string Status, int HouseholdId, string HouseholdName);

public record CreateHouseholdRequest(string HouseholdName, string Address, string City, string PostalCode,
    List<CreateMemberRequest> Members, List<CreateProgramParticipantRequest>? ProgramParticipants);

public record CreateMemberRequest(string FirstName, string LastName, string Email, string Phone,
    DateOnly? DateOfBirth, string RoleInHousehold);

public record CreateProgramParticipantRequest(string FirstName, string LastName, DateOnly DateOfBirth,
    string ProgramGroup, string? AllergiesOrNotes, string ParentContactEmail, string ParentContactPhone);

// ---------- Projects (umbrella that groups one or more Funds) ----------
public record ProjectDto(int Id, string Name, string Description, string Status, decimal? Budget,
    decimal RaisedAmount, DateOnly? StartDate, DateOnly? TargetCompletionDate, DateOnly? CompletedDate,
    string? ManagerName, string? ManagerContact, bool IsActive, int FundCount, List<FundDto> Funds);

public record CreateProjectRequest(string Name, string Description, string Status, decimal? Budget,
    DateOnly? StartDate, DateOnly? TargetCompletionDate, string? ManagerName, string? ManagerContact);

public record UpdateProjectStatusRequest(string Status, DateOnly? CompletedDate);

// ---------- Funds ----------
public record FundDto(int Id, string Name, string Description, string Category, decimal? GoalAmount,
    decimal RaisedAmount, bool IsActive, bool AllowNonMemberDonations, DateTime? EndDateUtc,
    int? ProjectId, string? ProjectName);

public record CreateFundRequest(string Name, string Description, string Category, decimal? GoalAmount,
    bool AllowNonMemberDonations, DateTime? EndDateUtc, int? ProjectId);

// ---------- Donations / Payments ----------
// Used by BOTH logged-in members and anonymous public donors.
public record CreatePaymentIntentRequest(
    int FundId,
    decimal Amount,
    string Currency,
    bool IsAnonymous,
    string DonorName,
    string DonorEmail,
    string? DonorPhone,
    int? EventId,
    string? Notes);

public record PaymentIntentResponse(string ClientSecret, int DonationId);

public record EmailPaymentLinkRequest(int FundId, decimal Amount, string RecipientName, string RecipientEmail, string? RecipientPhone);

public record DonationDto(int Id, decimal Amount, string Currency, string Status, DateTime CreatedAtUtc,
    string FundName, string DonorName, bool IsAnonymous, string? ReceiptUrl);

// ---------- Events ----------
public record LinkedProjectRef(int Id, string Name);

public record EventDto(int Id, string Title, string Description, DateTime StartUtc, DateTime? EndUtc,
    string Location, decimal? TicketPrice, int? Capacity, int RegisteredCount, bool OpenToPublic,
    List<LinkedProjectRef> LinkedProjects);

public record CreateEventRequest(string Title, string Description, DateTime StartUtc, DateTime? EndUtc,
    string Location, decimal? TicketPrice, int? Capacity, bool OpenToPublic, int? LinkedFundId, List<int>? ProjectIds);

public record RegisterForEventRequest(int EventId, string AttendeeName, string AttendeeEmail,
    string? AttendeePhone, int GuestCount);

// ---------- Contacts (non-members kept on file) ----------
public record ContactDto(int Id, string FirstName, string LastName, string? Email, string? Phone, string? Notes, DateTime CreatedAtUtc);
public record CreateContactRequest(string FirstName, string LastName, string? Email, string? Phone, string? Notes);

// ---------- Notifications ----------
public record NotificationGroupDto(int Id, string Name, string? Description, int RecipientCount);
public record CreateNotificationGroupRequest(string Name, string? Description);
public record AddGroupRecipientRequest(int? MemberId, int? ContactId, string? Email, string? Phone, string? DisplayName);

public record AdHocRecipientRequest(string Name, string? Email, string? Phone);
public record SendNotificationRequest(string Category, string Channel, string Subject, string Body,
    int? GroupId, List<int>? MemberIds, List<int>? ContactIds, List<AdHocRecipientRequest>? AdHocRecipients);

public record NotificationDeliveryDto(string RecipientName, string? Email, string? Phone, string Status, string? ErrorMessage);
public record NotificationDto(int Id, string Category, string Channel, string Subject, string Status,
    DateTime? SentAtUtc, int RecipientCount, int FailureCount, List<NotificationDeliveryDto>? Deliveries);

// ---------- Invoices & receipts ----------
public record InvoiceLineItemRequest(string Description, int Quantity, decimal UnitPrice);
public record CreateInvoiceRequest(int? MemberId, int? ContactId, string BillToName, string BillToEmail,
    DateOnly DueDate, string? Notes, int? FundId, int? ProjectId, List<InvoiceLineItemRequest> LineItems, bool CreatePaymentLink);

public record InvoiceDto(int Id, string InvoiceNumber, string Status, string BillToName, string BillToEmail,
    DateOnly IssueDate, DateOnly DueDate, decimal Total, string? Notes, string? StripePaymentLinkUrl,
    DateTime? SentAtUtc, List<InvoiceLineItemRequest> LineItems);

public record ReceiptDto(int Id, string ReceiptNumber, DateTime IssuedAtUtc, bool EmailedSuccessfully,
    decimal? Amount, string? DonorName, string? FundName);

// ---------- Meetings ----------
public record CreateMeetingRequest(string Title, string Type, DateTime ScheduledAtUtc, string Location, string? AgendaText);
public record RecordMinutesRequest(string MinutesText, List<MeetingAttendeeRequest> Attendees);
public record MeetingAttendeeRequest(int? MemberId, string Name, bool Attended);

public record MeetingAttendeeDto(int? MemberId, string Name, bool Attended);
public record MeetingDto(int Id, string Title, string Type, string Status, DateTime ScheduledAtUtc, string Location,
    string? AgendaText, string? MinutesText, DateTime? MinutesRecordedAtUtc, List<MeetingAttendeeDto> Attendees);

// ---------- Finance: expenses & income (project-based) ----------
public record CreateExpenseRequest(string Description, string Category, decimal Amount, string? Vendor,
    DateOnly ExpenseDate, int? ProjectId, int? FundId, string? ReceiptUrl);

public record ExpenseDto(int Id, string Description, string Category, decimal Amount, string? Vendor,
    DateOnly ExpenseDate, string Status, int? ProjectId, string? ProjectName, int? FundId, string? FundName, string? ReceiptUrl);

public record CreateIncomeRequest(string Source, string? Description, decimal Amount, DateOnly IncomeDate,
    string Method, int? ProjectId, int? FundId);

public record IncomeDto(int Id, string Source, string? Description, decimal Amount, DateOnly IncomeDate,
    string Method, int? ProjectId, string? ProjectName, int? FundId, string? FundName);

public record ProjectFinancialSummaryDto(int ProjectId, string ProjectName, decimal TotalDonations,
    decimal TotalManualIncome, decimal TotalIncome, decimal TotalExpenses, decimal NetBalance, decimal? Budget);

// ---------- Auth: refresh tokens & password reset ----------
public record RefreshTokenRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record AuthResponseWithRefresh(string Token, DateTime ExpiresAtUtc, string RefreshToken,
    string FullName, string Email, string[] Roles, int? MemberId);

public record AssignRoleRequest(string Role);

// ---------- Program registration (yearly student registration with sibling discounts) ----------
public record ProgramTermDto(int Id, string Name, DateOnly StartDate, DateOnly EndDate, decimal FeePerChild,
    bool IsOpenForRegistration, bool IsActive, int RegisteredChildCount);

public record CreateProgramTermRequest(string Name, DateOnly StartDate, DateOnly EndDate, decimal FeePerChild);

public record SiblingDiscountTierDto(int Id, int ChildPosition, decimal DiscountPercent);
public record UpsertSiblingDiscountTierRequest(int ChildPosition, decimal DiscountPercent);

// A single child as submitted on the registration form — no fee/discount yet, that's computed server-side
public record RegistrationChildInput(string FirstName, string LastName, DateOnly DateOfBirth,
    string ProgramGroup, string? AllergiesOrNotes);

public record RegistrationQuoteRequest(int ProgramTermId, List<RegistrationChildInput> Children);
public record RegistrationQuoteLineDto(string FirstName, string LastName, int ChildPositionInFamily,
    decimal BaseFee, decimal DiscountPercent, decimal FeeCharged);
public record RegistrationQuoteResponse(List<RegistrationQuoteLineDto> Lines, decimal TotalAmount);

public record CreateRegistrationIntentRequest(int ProgramTermId, List<RegistrationChildInput> Children);
public record RegistrationIntentResponse(string ClientSecret, int BatchId, decimal TotalAmount);

public record RegistrationChildDto(int Id, string FirstName, string LastName, DateOnly DateOfBirth,
    string ProgramGroup, int ChildPositionInFamily, decimal BaseFee, decimal DiscountPercentApplied, decimal FeeCharged);

public record RegistrationBatchDto(int Id, string ProgramTermName, string HouseholdName, string RegisteredByName,
    decimal TotalAmount, string Status, DateTime CreatedAtUtc, DateTime? PaidAtUtc, List<RegistrationChildDto> Children);

public record ProgramLevelDto(int Id, string Name, int SortOrder, bool IsActive);
public record CreateProgramLevelRequest(string Name, int SortOrder);


public record DashboardSummary(decimal TotalRaisedAllTime, decimal TotalRaisedThisMonth,
    int ActiveMembers, int TotalHouseholds, int UpcomingEvents, int ActiveProjects, List<FundDto> TopFunds);
