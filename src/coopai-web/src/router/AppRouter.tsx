import { BrowserRouter, Routes, Route } from "react-router-dom";
import MainLayout from "../layouts/MainLayout";

function DashboardPage() {
  return (
    <div className="rounded-lg bg-white p-6 shadow">
      <h2 className="text-2xl font-bold text-slate-800">
        Executive Dashboard
      </h2>

      <p className="mt-2 text-slate-600">
        ยินดีต้อนรับสู่ COOP-AI Smart Cooperative Platform
      </p>
    </div>
  );
}

export default function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<MainLayout />}>
          <Route path="/" element={<DashboardPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}