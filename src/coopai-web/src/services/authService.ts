import axios from "axios";
import { apiClient, getAntiforgeryHeaders } from "./apiClient";

export interface AuthUser {
    id: number;
    userName: string;
    displayName: string;
    roles: string[];
}

export async function getAntiforgeryToken() {
    const headers = await getAntiforgeryHeaders();
    return headers["X-CSRF-TOKEN"];
}

export async function login(userName: string, password: string) {
    const headers = await getAntiforgeryHeaders();
    const response = await apiClient.post<AuthUser>(
        "/api/auth/login",
        { userName, password },
        { headers },
    );
    return response.data;
}

export async function logout() {
    const headers = await getAntiforgeryHeaders();
    await apiClient.post(
        "/api/auth/logout",
        undefined,
        { headers },
    );
}

export async function getCurrentUser() {
    const response = await apiClient.get<AuthUser>("/api/auth/me");
    return response.data;
}

export function authErrorMessage(error: unknown) {
    if (axios.isAxiosError<{ message?: string }>(error))
        return error.response?.data?.message ?? "ไม่สามารถเข้าสู่ระบบได้ กรุณาลองใหม่อีกครั้ง";
    return "ไม่สามารถเข้าสู่ระบบได้ กรุณาลองใหม่อีกครั้ง";
}
