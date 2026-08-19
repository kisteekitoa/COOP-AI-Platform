import { useEffect, useMemo, useState, type ReactNode } from "react";

import { AuthContext, type AuthStatus } from "./AuthContext";
import {
    getCurrentUser,
    login as loginRequest,
    logout as logoutRequest,
    type AuthUser,
} from "../services/authService";

export default function AuthProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<AuthUser | null>(null);
    const [status, setStatus] = useState<AuthStatus>("loading");

    async function refresh() {
        try {
            const currentUser = await getCurrentUser();
            setUser(currentUser);
            setStatus("authenticated");
        } catch {
            setUser(null);
            setStatus("anonymous");
        }
    }

    useEffect(() => {
        void refresh();
    }, []);

    async function login(userName: string, password: string) {
        const authenticatedUser = await loginRequest(userName, password);
        setUser(authenticatedUser);
        setStatus("authenticated");
    }

    async function logout() {
        await logoutRequest();
        setUser(null);
        setStatus("anonymous");
    }

    const value = useMemo(
        () => ({ user, status, login, logout, refresh }),
        [user, status],
    );

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
