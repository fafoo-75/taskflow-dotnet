using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Helpers
{
    public class PaginatedList<T> : List<T>
    {
        public int PageIndex { get; private set; } // Num de page actuelle (commence à 1)

        public int TotalPages { get; private set; } // Nombre total de pages

        public int TotalCount { get; private set; } // Nb total d'élément dans la base (toutes pages confondues)

        private PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
        {
            PageIndex = pageIndex;
            TotalCount = count;
            // Math.Ceiling arrondit au supérieur :
            // 21 éléments / 10 par page = 2,1 -> 3 pages
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);

            this.AddRange(items); // On ajoute les éléments de cette page dans la liste héritée.
        }

        public bool HasPreviousPage => PageIndex > 1; // true si on est pas sur la 1er page

        public bool HasNextPage => PageIndex < TotalPages; // true si on est pas sur la dernière page

        public static async Task<PaginatedList<T>> CreateAsync(
            IQueryable<T> source,
            int pageIndex,
            int pageSize)
        {
            var count = await source.CountAsync();
            var items = await source
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return new PaginatedList<T>(items, count, pageIndex, pageSize);
        }
    }
}