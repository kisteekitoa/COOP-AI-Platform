export const workQueuePageSizes = [50, 100, 200] as const;
export type WorkQueuePageSize = (typeof workQueuePageSizes)[number];

export interface WorkQueueQuery {
    queueType: string;
    priority: string;
    debtBucket: string;
    payment: string;
    contractStatus: string;
    reason: string;
    search: string;
    page: number;
    pageSize: WorkQueuePageSize;
}

export const initialWorkQueueQuery: WorkQueueQuery = {
    queueType: "",
    priority: "",
    debtBucket: "",
    payment: "",
    contractStatus: "",
    reason: "",
    search: "",
    page: 1,
    pageSize: 50,
};

export function updateWorkQueueFilter<K extends Exclude<keyof WorkQueueQuery, "page" | "pageSize">>(
    query: WorkQueueQuery,
    key: K,
    value: WorkQueueQuery[K],
): WorkQueueQuery {
    return { ...query, [key]: value, page: 1 };
}

export function updateWorkQueuePageSize(
    query: WorkQueueQuery,
    pageSize: WorkQueuePageSize,
): WorkQueueQuery {
    return { ...query, pageSize, page: 1 };
}

export function workQueueResultRange(page: number, pageSize: number, totalCount: number) {
    if (totalCount === 0) return { first: 0, last: 0 };
    return {
        first: ((page - 1) * pageSize) + 1,
        last: Math.min(page * pageSize, totalCount),
    };
}
