export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;

export const PASSWORD_REQUIREMENTS_HINT =
  "Mínimo de 12 caracteres, com letra maiúscula, minúscula, número e caractere especial.";

export interface PasswordValidationResult {
  isValid: boolean;
  error: string | null;
}

export function validatePasswordComplexity(password: string): PasswordValidationResult {
  if (password.length < PASSWORD_MIN_LENGTH) {
    return {
      isValid: false,
      error: `A senha deve ter pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`,
    };
  }

  if (password.length > PASSWORD_MAX_LENGTH) {
    return {
      isValid: false,
      error: `A senha deve ter no máximo ${PASSWORD_MAX_LENGTH} caracteres.`,
    };
  }

  if (!/[A-Z]/.test(password)) {
    return {
      isValid: false,
      error: "A senha deve conter pelo menos uma letra maiúscula.",
    };
  }

  if (!/[a-z]/.test(password)) {
    return {
      isValid: false,
      error: "A senha deve conter pelo menos uma letra minúscula.",
    };
  }

  if (!/[0-9]/.test(password)) {
    return {
      isValid: false,
      error: "A senha deve conter pelo menos um número.",
    };
  }

  if (!/[^A-Za-z0-9]/.test(password)) {
    return {
      isValid: false,
      error: "A senha deve conter pelo menos um caractere especial.",
    };
  }

  return { isValid: true, error: null };
}

const identityErrorTranslations: Record<string, string> = {
  PasswordTooShort: `A senha deve ter pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`,
  PasswordRequiresDigit: "A senha deve conter pelo menos um número.",
  PasswordRequiresLower: "A senha deve conter pelo menos uma letra minúscula.",
  PasswordRequiresUpper: "A senha deve conter pelo menos uma letra maiúscula.",
  PasswordRequiresNonAlphanumeric: "A senha deve conter pelo menos um caractere especial.",
  PasswordRequiresUniqueChars: "A senha precisa de mais caracteres diferentes.",
};

export function translateApiProblem(
  problem: {
    title?: string;
    detail?: string;
    errors?: Record<string, string[]>;
  } | null,
  fallback: string,
): string {
  if (problem?.errors) {
    for (const [code, messages] of Object.entries(problem.errors)) {
      if (identityErrorTranslations[code]) {
        return identityErrorTranslations[code];
      }
      if (messages?.[0]) {
        return messages[0];
      }
    }
  }

  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  return fallback;
}
