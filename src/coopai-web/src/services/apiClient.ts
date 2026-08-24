import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export const apiClient = axios.create({
    baseURL: API_BASE_URL,
    withCredentials: true,
});

interface AntiforgeryResponse {
    requestToken: string;
}

export async function getAntiforgeryHeaders() {
    const response = await apiClient.get<AntiforgeryResponse>("/api/auth/csrf");
    return { "X-CSRF-TOKEN": response.data.requestToken };
}
