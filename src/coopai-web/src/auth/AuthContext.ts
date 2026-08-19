import { createContext } from "react";

import type { AuthUser } from "../services/authService";

export type AuthStatus = "loading" | "authenticated" | "anonymous";

export interface AuthContextValue {
    user: AuthUser | null;
    status: AuthStatus;
    login: (userName: string, password: string) => Promise<void>;
    logout: () => Promise<void>;
    refresh: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
