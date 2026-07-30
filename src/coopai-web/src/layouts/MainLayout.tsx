import { Outlet } from "react-router-dom";

export default function MainLayout() {
  return (
    <div className="min-h-screen bg-slate-100">
      {/* Header */}
      <header className="h-16 bg-green-700 text-white flex items-center px-6 shadow">
        <h1 className="text-xl font-bold">
          COOP-AI
        </h1>
      </header>

      {/* Main */}
      <main className="p-6">
        <Outlet />
      </main>
    </div>
  );
}