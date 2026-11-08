namespace Oy.Api.Domain;

public enum ProgramType { WeeklyClass, Camp, Event, Workshop }

public enum PricePeriod { Monthly, OneTime }

public enum EmploymentType { Salaried, PerLesson, Mixed }

public enum AssignmentRole { Lead, Assistant }

public enum EnrollmentStatus { Pending, Active, Cancelled, Completed }

public enum PaymentMethod { Cash, BankTransfer, Card, Online }

public enum InvoiceStatus { Pending, Sent, Failed }

public enum ThemeKind { Weekly, Monthly, SpecialDay }

public enum SpecialDayKind { PublicHoliday, ReligiousHoliday, Observance }

public enum IntegrationType { Payment, EInvoice }
