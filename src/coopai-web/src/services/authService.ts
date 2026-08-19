import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5171";
const authClient = axios.create({
    baseURL: `${API_BASE_URL}/api/auth`,
    withCredentials: true,
});

export interface AuthUser {
    id: number;
    userName: string;
    displayName: string;
    roles: string[];
}

interface AntiforgeryResponse {
    requestToken: string;
}

async function getAntiforgeryToken() {
    const response = await authClient.get<AntiforgeryResponse>("/csrf");
    return response.data.requestToken;
}

export async function login(userName: string, password: string) {
    const requestToken = await getAntiforgeryToken();
    const response = await authClient.post<AuthUser>(
        "/login",
        { userName, password },
        { headers: { "X-CSRF-TOKEN": requestToken } },
    );
    return response.data;
}

export async function logout() {
    const requestToken = await getAntiforgeryToken();
    await authClient.post(
        "/logout",
        undefined,
        { headers: { "X-CSRF-TOKEN": requestToken } },
    );
}

export async function getCurrentUser() {
    const response = await authClient.get<AuthUser>("/me");
    return response.data;
}

export function authErrorMessage(error: unknown) {
    if (axios.isAxiosError<{ message?: string }>(error))
        return error.response?.data?.message ?? "ไม่สามารถเข้าสู่ระบบได้ กรุณาลองใหม่อีกครั้ง";
    return "ไม่สามารถเข้าสู่ระบบได้ กรุณาลองใหม่อีกครั้ง";
}
