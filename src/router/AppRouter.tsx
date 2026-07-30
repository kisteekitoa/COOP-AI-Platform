import { BrowserRouter, Routes, Route } from "react-router-dom";

function DashboardPage() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100">
      <div className="text-center">
        <h1 className="text-4xl font-bold text-green-700">
          COOP-AI
        </h1>

        <p className="mt-3 text-slate-600">
          Smart Cooperative Platform
        </p>
      </div>
    </div>
  );
}

export default function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route
          path="/"
          element={<DashboardPage />}
        />
      </Routes>
    </BrowserRouter>
  );
}