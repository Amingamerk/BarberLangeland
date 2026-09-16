using BarberLangeland.Models;

namespace BarberLangeland.Persistence
{
    public interface IServiceRepository
    {
        Service? GetById(int id);

        List<Service> GetAll();

        Service? GetByName(string name);

        void Add(Service service);

        void Update(Service service);

        void Delete(int id);

        bool Exists(int id);

    }
}
