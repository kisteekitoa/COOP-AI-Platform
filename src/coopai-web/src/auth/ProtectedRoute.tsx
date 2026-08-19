import { Navigate, Outlet, useLocation } from "react-router-dom";

import { useAuth } from "./useAuth";

export default function ProtectedRoute() {
    const { status } = useAuth();
    const location = useLocation();

    if (status === "loading") {
        return (
            <div className="flex min-h-screen items-center justify-center bg-slate-100 px-6">
                <p className="text-sm text-slate-600">กำลังตรวจสอบสถานะการเข้าสู่ระบบ...</p>
            </div>
        );
    }

    if (status === "anonymous")
        return <Navigate to="/login" replace state={{ returnTo: location.pathname }} />;

    return <Outlet />;
}
