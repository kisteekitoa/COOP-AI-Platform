import { useState, type FormEvent } from "react";
import { LockKeyhole, LogIn, ShieldCheck } from "lucide-react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";

import { useAuth } from "../auth/useAuth";
import { authErrorMessage } from "../services/authService";

interface LoginLocationState {
    returnTo?: string;
}

export default function LoginPage() {
    const { login, status } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();
    const [userName, setUserName] = useState("");
    const [password, setPassword] = useState("");
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const returnTo = (location.state as LoginLocationState | null)?.returnTo ?? "/";

    if (status === "authenticated")
        return <Navigate to={returnTo} replace />;

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setIsSubmitting(true);
        setError(null);
        try {
            await login(userName, password);
            navigate(returnTo, { replace: true });
        } catch (requestError) {
            setError(authErrorMessage(requestError));
        } finally {
            setIsSubmitting(false);
        }
    }

    return (
        <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-emerald-950 via-green-900 to-slate-950 px-4 py-10 sm:px-6">
            <div className="grid w-full max-w-4xl overflow-hidden rounded-3xl bg-white shadow-2xl md:grid-cols-[1.05fr_1fr]">
                <section className="hidden bg-green-800 p-10 text-white md:flex md:flex-col md:justify-between">
                    <div>
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/15">
                            <ShieldCheck aria-hidden="true" size={28} />
                        </div>
                        <h1 className="mt-8 text-3xl font-bold">COOP-AI</h1>
                        <p className="mt-3 max-w-sm text-green-100">
                            ระบบบริหารจัดการข้อมูลสหกรณ์ที่ปกป้องข้อมูลด้วยบัญชีผู้ใช้ของ COOP-AI
                        </p>
                    </div>
                    <p className="text-sm text-green-200">Smart Cooperative Platform</p>
                </section>

                <section className="p-6 sm:p-10">
                    <div className="mx-auto max-w-sm">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-green-100 text-green-800 md:hidden">
                            <ShieldCheck aria-hidden="true" size={26} />
                        </div>
                        <p className="mt-6 text-sm font-semibold uppercase tracking-[0.2em] text-green-700">COOP-AI</p>
                        <h2 className="mt-2 text-3xl font-bold text-slate-900">เข้าสู่ระบบ</h2>
                        <p className="mt-2 text-sm text-slate-600">ใช้บัญชี COOP-AI ที่ได้รับอนุญาตจากผู้ดูแลระบบ</p>

                        <form className="mt-8 space-y-5" onSubmit={handleSubmit}>
                            <div>
                                <label className="text-sm font-medium text-slate-700" htmlFor="userName">ชื่อผู้ใช้</label>
                                <input
                                    id="userName"
                                    name="userName"
                                    type="text"
                                    autoComplete="username"
                                    required
                                    maxLength={100}
                                    value={userName}
                                    onChange={(event) => setUserName(event.target.value)}
                                    className="mt-2 w-full rounded-xl border border-slate-300 px-4 py-3 text-base outline-none transition focus:border-green-700 focus:ring-4 focus:ring-green-100"
                                />
                            </div>

                            <div>
                                <label className="text-sm font-medium text-slate-700" htmlFor="password">รหัสผ่าน</label>
                                <div className="relative mt-2">
                                    <LockKeyhole aria-hidden="true" className="pointer-events-none absolute left-4 top-3.5 text-slate-400" size={20} />
                                    <input
                                        id="password"
                                        name="password"
                                        type="password"
                                        autoComplete="current-password"
                                        required
                                        maxLength={256}
                                        value={password}
                                        onChange={(event) => setPassword(event.target.value)}
                                        className="w-full rounded-xl border border-slate-300 py-3 pl-12 pr-4 text-base outline-none transition focus:border-green-700 focus:ring-4 focus:ring-green-100"
                                    />
                                </div>
                            </div>

                            {error ? (
                                <div role="alert" className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                                    {error}
                                </div>
                            ) : null}

                            <button
                                type="submit"
                                disabled={isSubmitting || !userName.trim() || !password}
                                className="flex w-full items-center justify-center gap-2 rounded-xl bg-green-800 px-5 py-3 font-semibold text-white transition hover:bg-green-700 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                <LogIn aria-hidden="true" size={20} />
                                {isSubmitting ? "กำลังเข้าสู่ระบบ..." : "เข้าสู่ระบบ"}
                            </button>
                        </form>
                    </div>
                </section>
            </div>
        </main>
    );
}
